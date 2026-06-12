using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RiftServe64.Sdk.Networking;
using RiftServe64.Sdk.Protocol;

namespace RiftGate;

public sealed class RegisteredApp
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool UseTls { get; set; }
    public bool ValidateCertificate { get; set; }
}

public sealed class Bird
{
    public byte SpriteId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double VX { get; set; }
    public double VY { get; set; }
    public Rift64Color Color { get; set; }
}

public static class BirdFlock
{
    private static readonly Random Rnd = new();
    private static readonly List<Bird> Birds = new();

    public static void Initialize(int count)
    {
        Birds.Clear();
        var colors = new[] { Rift64Color.Cyan, Rift64Color.LightGreen, Rift64Color.Yellow, Rift64Color.LightBlue, Rift64Color.White, Rift64Color.Purple, Rift64Color.Orange, Rift64Color.LightRed };
        for (int i = 0; i < count; i++)
        {
            // C64 visible sprite coordinates
            // X: 24 to 344
            // Y: 50 to 250
            double x = Rnd.Next(60, 300);
            double y = Rnd.Next(80, 220);
            double angle = Rnd.NextDouble() * Math.PI * 2;
            double speed = Rnd.NextDouble() * 1.2 + 0.8;

            Birds.Add(new Bird
            {
                SpriteId = (byte)i, // Use all 8 sprites
                X = x,
                Y = y,
                VX = Math.Cos(angle) * speed,
                VY = Math.Sin(angle) * speed,
                Color = colors[i % colors.Length]
            });
        }
    }

    public static IReadOnlyDictionary<byte, (int X, byte Y)> Update()
    {
        var positions = new Dictionary<byte, (int X, byte Y)>();

        for (int i = 0; i < Birds.Count; i++)
        {
            var b = Birds[i];

            // 1. Cohesion: pull toward center of flock
            double avgX = 0;
            double avgY = 0;
            int count = 0;
            for (int j = 0; j < Birds.Count; j++)
            {
                if (i != j)
                {
                    avgX += Birds[j].X;
                    avgY += Birds[j].Y;
                    count++;
                }
            }
            if (count > 0)
            {
                avgX /= count;
                avgY /= count;
                b.VX += (avgX - b.X) * 0.003;
                b.VY += (avgY - b.Y) * 0.003;
            }

            // 2. Separation: steer away if too close
            for (int j = 0; j < Birds.Count; j++)
            {
                if (i != j)
                {
                    double dx = b.X - Birds[j].X;
                    double dy = b.Y - Birds[j].Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 30 && dist > 0)
                    {
                        b.VX += (dx / dist) * 0.12;
                        b.VY += (dy / dist) * 0.12;
                    }
                }
            }

            // 3. Screen bounds restriction (smooth rebound)
            if (b.X < 35) b.VX += 0.18;
            if (b.X > 315) b.VX -= 0.18;
            if (b.Y < 55) b.VY += 0.18;
            if (b.Y > 235) b.VY -= 0.18;

            // 4. Subtle random organic flocking drift
            b.VX += (Rnd.NextDouble() - 0.5) * 0.25;
            b.VY += (Rnd.NextDouble() - 0.5) * 0.25;

            // 5. Constrain velocity
            double speed = Math.Sqrt(b.VX * b.VX + b.VY * b.VY);
            double maxSpeed = 3.0;
            double minSpeed = 0.6;
            if (speed > maxSpeed)
            {
                b.VX = (b.VX / speed) * maxSpeed;
                b.VY = (b.VY / speed) * maxSpeed;
            }
            else if (speed < minSpeed)
            {
                b.VX = (b.VX / speed) * minSpeed;
                b.VY = (b.VY / speed) * minSpeed;
            }

            b.X += b.VX;
            b.Y += b.VY;

            positions[b.SpriteId] = ((int)b.X, (byte)b.Y);
        }

        return positions;
    }

    public static List<Bird> GetBirds() => Birds;
}

public sealed class PassThroughTextConverter : IRift64TextConverter
{
    public static PassThroughTextConverter Default { get; } = new();

    public byte[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var output = new byte[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '\r' or '\n')
            {
                output[i] = 13;
            }
            else if (c == '\t')
            {
                output[i] = (byte)' ';
            }
            else if (c is >= 'a' and <= 'z')
            {
                output[i] = (byte)(c - 32);
            }
            else
            {
                output[i] = (byte)c;
            }
        }

        return output;
    }

    public char DecodeByte(byte value)
    {
        return (char)value;
    }

    public string Decode(ReadOnlySpan<byte> values)
    {
        if (values.IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(values.Length);
        foreach (var value in values)
        {
            builder.Append(DecodeByte(value));
        }

        return builder.ToString();
    }
}

public static class Program
{
    private const int Port = 8000;
    private const int DefaultMaxConnections = 8;
    private const int PageSize = 10; // Height of scrolling window
    private const string AppsFileName = "apps.csv";
    private const string AdminConfigFileName = "riftgate-admin.json";
    private const string TitleText = "RIFTGATE";
    private static readonly Rift64Color[] HeaderColors = [
        Rift64Color.DarkGray,
        Rift64Color.MediumGray,
        Rift64Color.LightGray,
        Rift64Color.White,
        Rift64Color.LightGray,
        Rift64Color.MediumGray,
        Rift64Color.DarkGray,
        Rift64Color.Blue,
        Rift64Color.LightBlue,
        Rift64Color.Cyan,
        Rift64Color.LightBlue,
        Rift64Color.Blue
    ];
    private const byte TitleWaveRow = 1;
    private const byte TitleWaveLeftX = 3;
    private const byte TitleWaveRightX = 25;
    private const int TitleWaveWidth = 12;
    private static readonly byte[] TitleWaveGlyphs = [100, 111, 121, 98, 248, 247, 227, 160];

    public static async Task Main()
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("                RIFT GATE PROXY SERVER            ");
        Console.WriteLine("==================================================");

        var appsPath = ResolveWorkingFilePath(AppsFileName);
        var configPath = ResolveWorkingFilePath(AdminConfigFileName);
        var settingsStore = RiftGateSettingsStore.LoadOrCreate(configPath, appsPath, DefaultMaxConnections);
        var apps = LoadApps(appsPath);
        Console.WriteLine($"Loaded {apps.Count} registered apps from CSV.");

        var runtimeSettings = settingsStore.GetRuntimeSettings();
        var server = SocketServer.GetOrCreate("0.0.0.0", Port, maxConnections: runtimeSettings.MaxConnections);
        await using var adminServer = new RiftGateAdminWebServer(settingsStore);
        await adminServer.StartAsync().ConfigureAwait(false);
        Console.WriteLine($"RiftGate admin UI available at {adminServer.BaseUrl}");
        Console.WriteLine($"Admin config: {settingsStore.ConfigPath}");

        server.ClientConnected += connection =>
        {
            _ = Task.Run(async () =>
            {
                var clientEndpoint = connection.RemoteEndPoint?.ToString() ?? "Unknown";
                Console.WriteLine($"[Client connected] {clientEndpoint}");

                try
                {
                    var client = new Rift64ProtocolClient(connection, PassThroughTextConverter.Default);

                    var identity = await client.IdentifyClientAsync().ConfigureAwait(false);
                    if (!identity.IsCompatible)
                    {
                        await client.ClearScreenAsync().ConfigureAwait(false);
                        await client.SetCursorAsync(0, 0).ConfigureAwait(false);
                        await client.WriteTextAsync("Unsupported client. Expected RIFT64.", CancellationToken.None).ConfigureAwait(false);
                        return;
                    }

                    Console.WriteLine($"[Handshake successful] {clientEndpoint} is running C64 Client v{identity.ClientVersion}");

                    // Turn off cursor visibility immediately
                    await client.SetCursorVisibilityAsync(false, CancellationToken.None).ConfigureAwait(false);

                    // Run the interactive RIFT Gate menu
                    await RunMenuLoopAsync(connection, client, apps, settingsStore).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Session Error] {clientEndpoint}: {ex.Message}");
                }
                finally
                {
                    Console.WriteLine($"[Client disconnected] {clientEndpoint}");
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
            });

            return ValueTask.CompletedTask;
        };

        await server.StartAsync().ConfigureAwait(false);
        Console.WriteLine($"RIFT Gate is active and listening on 0.0.0.0:{Port}");
        Console.WriteLine("Press Ctrl+C to terminate the gateway.");

        try
        {
            await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
        }
        finally
        {
            await server.StopAsync().ConfigureAwait(false);
        }
    }

    private static async Task RunMenuLoopAsync(
        IClientConnection connection,
        Rift64ProtocolClient client,
        List<RegisteredApp> apps,
        RiftGateSettingsStore settingsStore)
    {
        var cancellationToken = CancellationToken.None;

        if (apps.Count == 0)
        {
            await client.ClearScreenAsync(cancellationToken).ConfigureAwait(false);
            await client.SetColorsAsync(Rift64Color.Black, Rift64Color.Black, cancellationToken).ConfigureAwait(false);
            await client.WriteAtAsync(2, 6, "No registered apps found in apps.csv.", Rift64Color.Red, cancellationToken).ConfigureAwait(false);
            await client.WriteAtAsync(2, 8, "Please define some servers and reconnect.", Rift64Color.Yellow, cancellationToken).ConfigureAwait(false);
            await client.WriteAtAsync(2, 10, "Press any key to retry.", Rift64Color.White, cancellationToken).ConfigureAwait(false);
            await client.ReadKeyAsync(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
            return;
        }

        var runtimeSettings = settingsStore.GetRuntimeSettings();
        await RestoreGateTakeoverStateAsync(client, runtimeSettings, cancellationToken).ConfigureAwait(false);

        // Active State
        var filteredApps = new List<RegisteredApp>(apps);
        string searchQuery = string.Empty;
        long observedSettingsVersion = settingsStore.Version;

        int selectedIndex = 0;
        int viewportStart = 0;
        bool forceFullRedraw = true;
        var wavePhase = 0;
        var waveFrame = 0;
        var headerColorIndex = 0;
        var colorCycleFrame = 0;
        var spriteAnimationsEnabled = runtimeSettings.EnableSpriteAnimation;
        var titleFaderActive = runtimeSettings.EnableTitleFader;

        while (connection.IsConnected)
        {
            runtimeSettings = settingsStore.GetRuntimeSettings();
            if (settingsStore.Version != observedSettingsVersion)
            {
                observedSettingsVersion = settingsStore.Version;
                apps = LoadApps(settingsStore.AppsCsvPath);
                filteredApps = FilterApps(apps, searchQuery);

                if (selectedIndex >= filteredApps.Count)
                {
                    selectedIndex = filteredApps.Count == 0 ? 0 : filteredApps.Count - 1;
                }

                viewportStart = filteredApps.Count <= PageSize
                    ? 0
                    : Math.Max(0, Math.Min(selectedIndex, filteredApps.Count - PageSize));

                forceFullRedraw = true;
            }

            if (forceFullRedraw)
            {
                await RenderFullMenuAsync(client, filteredApps, selectedIndex, viewportStart, PageSize, searchQuery, cancellationToken).ConfigureAwait(false);
                forceFullRedraw = false;
            }

            if (runtimeSettings.EnableWaveAnimation)
            {
                await RenderTitleWaveAsync(client, wavePhase, cancellationToken).ConfigureAwait(false);
                waveFrame = (waveFrame + 1) & 1;
                if (waveFrame == 0)
                {
                    wavePhase = (wavePhase + 1) % (TitleWaveGlyphs.Length * 8);
                }
            }
            else
            {
                await ClearTitleWaveAsync(client, cancellationToken).ConfigureAwait(false);
            }

            // Continuous color cycling for the RIFT GATE title text (shades of grey -> shades of blue -> repeat)
            // Throttled to update once every 4 frames to preserve serial interface bandwidth
            if (runtimeSettings.EnableTitleFader)
            {
                colorCycleFrame = (colorCycleFrame + 1) % 4;
                if (colorCycleFrame == 0)
                {
                    int titleX = (40 - TitleText.Length) / 2;
                    await client.WriteAtAsync((byte)titleX, 1, TitleText, HeaderColors[headerColorIndex], cancellationToken).ConfigureAwait(false);
                    headerColorIndex = (headerColorIndex + 1) % HeaderColors.Length;
                }
                titleFaderActive = true;
            }
            else if (titleFaderActive)
            {
                int titleX = (40 - TitleText.Length) / 2;
                await client.WriteAtAsync((byte)titleX, 1, TitleText, Rift64Color.White, cancellationToken).ConfigureAwait(false);
                titleFaderActive = false;
            }

            if (runtimeSettings.EnableSpriteAnimation)
            {
                if (!spriteAnimationsEnabled)
                {
                    await InitializeMenuSpritesAsync(client, cancellationToken).ConfigureAwait(false);
                    spriteAnimationsEnabled = true;
                }

                try
                {
                    var positions = BirdFlock.Update();
                    await client.SetSpritePositionsAsync(positions, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception) { }
            }
            else if (spriteAnimationsEnabled)
            {
                await DisableSpritesAsync(client, cancellationToken).ConfigureAwait(false);
                spriteAnimationsEnabled = false;
            }

            // Fast poll to maintain 12+ FPS background animation
            var keyChar = await client.ReadKeyAsync(TimeSpan.FromMilliseconds(80), cancellationToken).ConfigureAwait(false);
            if (keyChar == null)
            {
                continue;
            }

            byte key = (byte)keyChar.Value;
            int oldIndex = selectedIndex;
            int totalApps = filteredApps.Count;

            if (key == 17) // CRSR DOWN (implicit selection move)
            {
                if (totalApps > 0)
                {
                    selectedIndex = (selectedIndex + 1) % totalApps;
                }
            }
            else if (key == 145) // CRSR UP (implicit selection move)
            {
                if (totalApps > 0)
                {
                    selectedIndex = (selectedIndex - 1 + totalApps) % totalApps;
                }
            }
            else if (key == 133) // [F1] Key triggers keyword search
            {
                string? newQuery = await RunSearchRoutineAsync(client, searchQuery, cancellationToken).ConfigureAwait(false);
                if (newQuery != null)
                {
                    searchQuery = newQuery.Trim();
                    filteredApps = FilterApps(apps, searchQuery);

                    // Reset selection & scroll viewport
                    selectedIndex = 0;
                    viewportStart = 0;
                }

                forceFullRedraw = true;
            }
            else if (key is 134 or 13 or 10) // [F3] or RETURN triggers Connection
            {
                if (selectedIndex < totalApps)
                {
                    await DisableSpritesAsync(client, cancellationToken).ConfigureAwait(false);

                    var selectedApp = filteredApps[selectedIndex];
                    await RunProxySessionAsync(connection, client, selectedApp, cancellationToken).ConfigureAwait(false);

                    // Reload csv & reapply search filter upon return
                    apps = LoadApps(settingsStore.AppsCsvPath);
                    filteredApps = FilterApps(apps, searchQuery);

                    totalApps = filteredApps.Count;
                    if (selectedIndex >= totalApps)
                    {
                        selectedIndex = 0;
                    }
                    viewportStart = Math.Max(0, Math.Min(selectedIndex, totalApps - PageSize));

                    runtimeSettings = settingsStore.GetRuntimeSettings();
                    await RestoreGateTakeoverStateAsync(client, runtimeSettings, cancellationToken).ConfigureAwait(false);
                    spriteAnimationsEnabled = runtimeSettings.EnableSpriteAnimation;
                    observedSettingsVersion = settingsStore.Version;

                    forceFullRedraw = true;
                }
            }
            else if (key is 136 or (byte)'q' or (byte)'Q') // [F7] or Q triggers Quit
            {
                await DisableSpritesAsync(client, cancellationToken).ConfigureAwait(false);
                await client.ClearScreenAsync(cancellationToken).ConfigureAwait(false);
                await client.SetCursorAsync(0, 0, cancellationToken).ConfigureAwait(false);
                await client.WriteTextAsync("Goodbye from Rift Gate!", cancellationToken).ConfigureAwait(false);
                break;
            }

            if (!forceFullRedraw && selectedIndex != oldIndex && totalApps > 0)
            {
                if (totalApps <= PageSize)
                {
                    // No region scrolling needed, list fits on one viewport screen
                    viewportStart = 0;
                    await RenderSelectionChangeAsync(client, filteredApps, oldIndex, selectedIndex, viewportStart, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // Viewport boundary scrolling
                    if (selectedIndex == 0 && oldIndex == totalApps - 1)
                    {
                        // Wrap-around jump from bottom to top (fast menu items refresh only)
                        viewportStart = 0;
                        await RefreshMenuItemsOnlyAsync(client, filteredApps, selectedIndex, viewportStart, PageSize, cancellationToken).ConfigureAwait(false);
                    }
                    else if (selectedIndex == totalApps - 1 && oldIndex == 0)
                    {
                        // Wrap-around jump from top to bottom (fast menu items refresh only)
                        viewportStart = totalApps - PageSize;
                        await RefreshMenuItemsOnlyAsync(client, filteredApps, selectedIndex, viewportStart, PageSize, cancellationToken).ConfigureAwait(false);
                    }
                    else if (selectedIndex >= viewportStart + PageSize)
                    {
                        // Shift viewport down (Scroll list contents UP by 1)
                        viewportStart = selectedIndex - PageSize + 1;

                        // Perform Hardware/Firmware Region Scroll UP
                        await client.ScrollRegionAsync(2, 5, 36, 10, Rift64ScrollDirection.Up, cancellationToken).ConfigureAwait(false);

                        // Old item was on row 14, now scrolled UP to row 13. Redraw it unselected.
                        byte oldRowOnScreen = 13;
                        var oldApp = filteredApps[oldIndex];
                        await RenderMenuRowAsync(client, oldRowOnScreen, oldIndex, oldApp, isSelected: false, cancellationToken).ConfigureAwait(false);

                        // New item is at row 14 (bottom viewport row). Draw it selected.
                        byte newRowOnScreen = 14;
                        var newApp = filteredApps[selectedIndex];
                        await RenderMenuRowAsync(client, newRowOnScreen, selectedIndex, newApp, isSelected: true, cancellationToken).ConfigureAwait(false);

                        await UpdateDescriptionBoxAsync(client, newApp, cancellationToken).ConfigureAwait(false);
                    }
                    else if (selectedIndex < viewportStart)
                    {
                        // Shift viewport up (Scroll list contents DOWN by 1)
                        viewportStart = selectedIndex;

                        // Perform Region Scroll DOWN
                        await client.ScrollRegionAsync(2, 5, 36, 10, Rift64ScrollDirection.Down, cancellationToken).ConfigureAwait(false);

                        // Old item was on row 5, now scrolled DOWN to row 6. Redraw it unselected.
                        byte oldRowOnScreen = 6;
                        var oldApp = filteredApps[oldIndex];
                        await RenderMenuRowAsync(client, oldRowOnScreen, oldIndex, oldApp, isSelected: false, cancellationToken).ConfigureAwait(false);

                        // New item is at row 5 (top viewport row). Draw it selected.
                        byte newRowOnScreen = 5;
                        var newApp = filteredApps[selectedIndex];
                        await RenderMenuRowAsync(client, newRowOnScreen, selectedIndex, newApp, isSelected: true, cancellationToken).ConfigureAwait(false);

                        await UpdateDescriptionBoxAsync(client, newApp, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        // Selection moved within viewport (no scroll shifts)
                        await RenderSelectionChangeAsync(client, filteredApps, oldIndex, selectedIndex, viewportStart, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    private static async Task<string?> RunSearchRoutineAsync(
        Rift64ProtocolClient client,
        string currentQuery,
        CancellationToken cancellationToken)
    {
        try
        {
            // Clear subheader area and draw Search prompt
            await client.WriteAtAsync(0, 3, "                                        ", Rift64Color.Black, cancellationToken).ConfigureAwait(false);
            await client.WriteAtAsync(1, 3, "Search: [                            ]", Rift64Color.LightGreen, cancellationToken).ConfigureAwait(false);

            await client.SetCursorVisibilityAsync(true, cancellationToken).ConfigureAwait(false);

            string searchQuery = currentQuery;
            while (true)
            {
                string displayQuery = searchQuery.PadRight(28).Substring(0, 28);
                await client.WriteAtAsync(10, 3, displayQuery, Rift64Color.Yellow, cancellationToken).ConfigureAwait(false);

                await client.SetCursorAsync((byte)(10 + Math.Min(searchQuery.Length, 27)), 3, cancellationToken).ConfigureAwait(false);

                var keyChar = await client.ReadKeyAsync(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
                if (keyChar == null)
                {
                    continue;
                }

                byte key = (byte)keyChar.Value;

                if (key is 13 or 10) // ENTER
                {
                    break;
                }
                else if (key == 20) // BACKSPACE / DELETE
                {
                    if (searchQuery.Length > 0)
                    {
                        searchQuery = searchQuery.Substring(0, searchQuery.Length - 1);
                    }
                }
                else if (key is 136 or 3) // F7 or RUN-STOP (Cancel search)
                {
                    await client.SetCursorVisibilityAsync(false, cancellationToken).ConfigureAwait(false);
                    return null;
                }
                else if (keyChar.Value >= ' ' && keyChar.Value <= '~' && searchQuery.Length < 27)
                {
                    searchQuery += keyChar.Value;
                }
            }

            await client.SetCursorVisibilityAsync(false, cancellationToken).ConfigureAwait(false);
            return searchQuery;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task RenderFullMenuAsync(
        Rift64ProtocolClient client,
        List<RegisteredApp> apps,
        int selectedIndex,
        int viewportStart,
        int pageSize,
        string searchQuery,
        CancellationToken cancellationToken)
    {
        await client.ClearScreenAsync(cancellationToken).ConfigureAwait(false);
        await client.SetColorsAsync(Rift64Color.Black, Rift64Color.Cyan, cancellationToken).ConfigureAwait(false);

        // Initialize wave color RAM once to LightBlue to avoid redundant frame updates
        var waveColors = new byte[TitleWaveWidth];
        Array.Fill(waveColors, (byte)Rift64Color.LightBlue);
        var leftWaveOffset = TitleWaveRow * 40 + TitleWaveLeftX;
        var rightWaveOffset = TitleWaveRow * 40 + TitleWaveRightX;
        await client.StoreMemoryAsync((ushort)(0xD800 + leftWaveOffset), waveColors, cancellationToken).ConfigureAwait(false);
        await client.StoreMemoryAsync((ushort)(0xD800 + rightWaveOffset), waveColors, cancellationToken).ConfigureAwait(false);

        // --- Title Frame (Top Box) ---
        await client.SetCursorAsync(1, 0, cancellationToken).ConfigureAwait(false);
        await client.DrawBorderAsync(38, 3, Rift64BorderGlyphs.Default, cancellationToken).ConfigureAwait(false);

        // Title text centered inside
        int titleX = (40 - TitleText.Length) / 2;
        await client.WriteAtAsync((byte)titleX, 1, TitleText, Rift64Color.White, cancellationToken).ConfigureAwait(false);

        // Color legend for the red, green, and yellow indicators
        // Drawn at explicit, symmetrical column positions for perfect alignment (columns 3, 16, 29)
        byte[] badgeChars = { 27, 81, 29 }; // Screen codes for '[', ball, ']'
        ushort screenBase = 0x0400 + 3 * 40;
        ushort colorBase = 0xD800 + 3 * 40;

        // 1. SECURE CA (Columns 3..12)
        await client.WriteAtAsync(7, 3, "SECURE", Rift64Color.LightGray, cancellationToken).ConfigureAwait(false);
        await client.StoreMemoryAsync((ushort)(screenBase + 3), badgeChars, cancellationToken).ConfigureAwait(false);
        await client.StoreMemoryAsync((ushort)(colorBase + 3), new byte[] { (byte)Rift64Color.LightGray, (byte)Rift64Color.LightGreen, (byte)Rift64Color.LightGray }, cancellationToken).ConfigureAwait(false);

        // 2. SS-TLS (Columns 16..25)
        await client.WriteAtAsync(20, 3, "SS-TLS", Rift64Color.LightGray, cancellationToken).ConfigureAwait(false);
        await client.StoreMemoryAsync((ushort)(screenBase + 16), badgeChars, cancellationToken).ConfigureAwait(false);
        await client.StoreMemoryAsync((ushort)(colorBase + 16), new byte[] { (byte)Rift64Color.LightGray, (byte)Rift64Color.Yellow, (byte)Rift64Color.LightGray }, cancellationToken).ConfigureAwait(false);

        // 3. PLAIN (Columns 29..37)
        await client.WriteAtAsync(33, 3, "PLAIN", Rift64Color.LightGray, cancellationToken).ConfigureAwait(false);
        await client.StoreMemoryAsync((ushort)(screenBase + 29), badgeChars, cancellationToken).ConfigureAwait(false);
        await client.StoreMemoryAsync((ushort)(colorBase + 29), new byte[] { (byte)Rift64Color.LightGray, (byte)Rift64Color.Red, (byte)Rift64Color.LightGray }, cancellationToken).ConfigureAwait(false);

        // --- App Selector Box ---
        await client.SetCursorAsync(1, 4, cancellationToken).ConfigureAwait(false);
        await client.DrawBorderAsync(38, 12, Rift64BorderGlyphs.Default, cancellationToken).ConfigureAwait(false);

        // Render viewport items inside the box
        byte startRow = 5;

        for (int i = 0; i < pageSize; i++)
        {
            byte row = (byte)(startRow + i);
            int appIdx = viewportStart + i;

            if (appIdx < apps.Count)
            {
                var app = apps[appIdx];
                bool isSelected = (appIdx == selectedIndex);
                await RenderMenuRowAsync(client, row, appIdx, app, isSelected, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (i == 0 && apps.Count == 0)
                {
                    await client.WriteAtAsync(2, row, "      No Matching Apps Found.       ", Rift64Color.Red, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await client.WriteAtAsync(2, row, new string(' ', 36), Rift64Color.Black, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        // --- Description Box ---
        await client.SetCursorAsync(1, 16, cancellationToken).ConfigureAwait(false);
        await client.DrawBorderAsync(38, 7, Rift64BorderGlyphs.Default, cancellationToken).ConfigureAwait(false);

        if (selectedIndex < apps.Count)
        {
            var selectedApp = apps[selectedIndex];
            await UpdateDescriptionBoxAsync(client, selectedApp, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Clear description box
            for (byte r = 17; r <= 21; r++)
            {
                await client.WriteAtAsync(2, r, new string(' ', 36), Rift64Color.White, cancellationToken).ConfigureAwait(false);
            }
        }

        // --- Help Bar ---
        string statusText = "  F1: Search    F3: Connect    F7: Quit ";
        await client.WriteAtAsync(0, 24, statusText, Rift64Color.LightBlue, cancellationToken).ConfigureAwait(false);

        // Hide cursor to prevent it blinking at the bottom left
        await client.SetCursorVisibilityAsync(false, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RenderTitleWaveAsync(
        Rift64ProtocolClient client,
        int phase,
        CancellationToken cancellationToken)
    {
        var leftWave = BuildWaveBytes(TitleWaveWidth, phase, mirrored: false);
        var rightWave = BuildWaveBytes(TitleWaveWidth, phase, mirrored: true);

        var leftOffset = TitleWaveRow * 40 + TitleWaveLeftX;
        var rightOffset = TitleWaveRow * 40 + TitleWaveRightX;

        await client.StoreMemoryAsync((ushort)(0x0400 + leftOffset), leftWave, cancellationToken).ConfigureAwait(false);
        await client.StoreMemoryAsync((ushort)(0x0400 + rightOffset), rightWave, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ClearTitleWaveAsync(
        Rift64ProtocolClient client,
        CancellationToken cancellationToken)
    {
        var spaces = new byte[TitleWaveWidth];
        Array.Fill(spaces, (byte)32);

        var leftOffset = TitleWaveRow * 40 + TitleWaveLeftX;
        var rightOffset = TitleWaveRow * 40 + TitleWaveRightX;

        await client.StoreMemoryAsync((ushort)(0x0400 + leftOffset), spaces, cancellationToken).ConfigureAwait(false);
        await client.StoreMemoryAsync((ushort)(0x0400 + rightOffset), spaces, cancellationToken).ConfigureAwait(false);
    }

    private static byte[] BuildWaveBytes(int width, int phase, bool mirrored)
    {
        var bytes = new byte[width];

        for (var i = 0; i < width; i++)
        {
            var sampleIndex = mirrored ? (width - 1 - i) : i;
            var angle = ((sampleIndex + phase) * 0.55) % (Math.PI * 2);
            var normalized = (Math.Sin(angle) + 1.0) * 0.5;
            var glyphIndex = Math.Clamp((int)Math.Round(normalized * (TitleWaveGlyphs.Length - 1)), 0, TitleWaveGlyphs.Length - 1);
            bytes[i] = TitleWaveGlyphs[glyphIndex];
        }

        return bytes;
    }

    private static async Task RenderSelectionChangeAsync(
        Rift64ProtocolClient client,
        List<RegisteredApp> apps,
        int oldSelectedIndex,
        int newSelectedIndex,
        int viewportStart,
        CancellationToken cancellationToken)
    {
        byte startRow = 5;

        // Unhighlight old item
        int oldPageIdx = oldSelectedIndex - viewportStart;
        byte oldRow = (byte)(startRow + oldPageIdx);
        var oldApp = apps[oldSelectedIndex];
        await RenderMenuRowAsync(client, oldRow, oldSelectedIndex, oldApp, isSelected: false, cancellationToken).ConfigureAwait(false);

        // Highlight new item
        int newPageIdx = newSelectedIndex - viewportStart;
        byte newRow = (byte)(startRow + newPageIdx);
        var newApp = apps[newSelectedIndex];
        await RenderMenuRowAsync(client, newRow, newSelectedIndex, newApp, isSelected: true, cancellationToken).ConfigureAwait(false);

        // Update description
        await UpdateDescriptionBoxAsync(client, newApp, cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpdateDescriptionBoxAsync(
        Rift64ProtocolClient client,
        RegisteredApp app,
        CancellationToken cancellationToken)
    {
        // Clear description text area
        for (byte r = 17; r <= 21; r++)
        {
            await client.WriteAtAsync(2, r, new string(' ', 36), Rift64Color.White, cancellationToken).ConfigureAwait(false);
        }

        // Render new description
        var wrappedLines = WordWrap(app.Description, 36);
        for (int i = 0; i < Math.Min(wrappedLines.Count, 5); i++)
        {
            await client.WriteAtAsync(2, (byte)(17 + i), wrappedLines[i], Rift64Color.White, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task RefreshMenuItemsOnlyAsync(
        Rift64ProtocolClient client,
        List<RegisteredApp> apps,
        int selectedIndex,
        int viewportStart,
        int pageSize,
        CancellationToken cancellationToken)
    {
        byte startRow = 5;

        for (int i = 0; i < pageSize; i++)
        {
            byte row = (byte)(startRow + i);
            int appIdx = viewportStart + i;

            if (appIdx < apps.Count)
            {
                var app = apps[appIdx];
                bool isSelected = (appIdx == selectedIndex);
                await RenderMenuRowAsync(client, row, appIdx, app, isSelected, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await client.WriteAtAsync(2, row, new string(' ', 36), Rift64Color.Black, cancellationToken).ConfigureAwait(false);
            }
        }

        // Update description box
        if (selectedIndex < apps.Count)
        {
            var selectedApp = apps[selectedIndex];
            await UpdateDescriptionBoxAsync(client, selectedApp, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task DisableSpritesAsync(Rift64ProtocolClient client, CancellationToken cancellationToken)
    {
        try
        {
            for (byte i = 0; i < 8; i++)
            {
                await client.SetSpriteAsync(i, 0, 0, Rift64Color.Black, 0, bank: VicBank.Bank0, enabled: false, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sprites] Error shutting down: {ex.Message}");
        }
    }

    private static async Task InitializeMenuSpritesAsync(Rift64ProtocolClient client, CancellationToken cancellationToken)
    {
        var dotPattern = new byte[64];
        dotPattern[10 * 3 + 1] = 0x18;
        dotPattern[11 * 3 + 1] = 0x18;

        await client.UploadSpriteAsync(VicBank.Bank0, 13, dotPattern, cancellationToken).ConfigureAwait(false);

        BirdFlock.Initialize(8);

        foreach (var bird in BirdFlock.GetBirds())
        {
            await client.SetSpriteMulticolorAsync(
                spriteId: bird.SpriteId,
                x: (int)bird.X,
                y: (byte)bird.Y,
                color: bird.Color,
                pointer: 13,
                bank: VicBank.Bank0,
                enabled: true,
                multicolor: false,
                expandX: false,
                expandY: false,
                priority: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task RestoreGateTakeoverStateAsync(
        Rift64ProtocolClient client,
        RiftGateRuntimeSettings settings,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("[RiftGate] Reclaiming C64 state...");

        try
        {
            await client.StopSongAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RiftGate] Audio stop failed: {ex.Message}");
        }

        try
        {
            var defaultLayout = VicLayout.ForText(VicBank.Bank0, VicScreenSlot.Slot1, VicCharsetSlot.Slot2);
            await client.ApplyVicLayoutAsync(
                defaultLayout,
                border: Rift64Color.Black,
                background: Rift64Color.Black,
                timeout: TimeSpan.FromSeconds(2),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await client.SetCursorVisibilityAsync(false, cancellationToken).ConfigureAwait(false);
            await client.ClearScreenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RiftGate] VIC reset failed: {ex.Message}");
        }

        try
        {
            if (settings.EnableSpriteAnimation)
            {
                await InitializeMenuSpritesAsync(client, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await DisableSpritesAsync(client, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RiftGate] Sprite recreation failed: {ex.Message}");
        }
    }

    private static async Task RunProxySessionAsync(
        IClientConnection connection,
        Rift64ProtocolClient client,
        RegisteredApp app,
        CancellationToken sessionCancellationToken)
    {
        Console.WriteLine($"[Proxy] Starting connection to {app.Name} ({app.Host}:{app.Port})");

        try
        {
            await client.ClearScreenAsync(sessionCancellationToken).ConfigureAwait(false);
            await client.WriteAtAsync(0, 10, "========================================", Rift64Color.Cyan, sessionCancellationToken).ConfigureAwait(false);
            await client.WriteAtAsync(0, 11, $"Connecting to: {app.Name}...", Rift64Color.White, sessionCancellationToken).ConfigureAwait(false);
            await client.WriteAtAsync(0, 12, "========================================", Rift64Color.Cyan, sessionCancellationToken).ConfigureAwait(false);

            using var remoteClient = new TcpClient();
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(5));

            await remoteClient.ConnectAsync(app.Host, app.Port, connectCts.Token).ConfigureAwait(false);
            var baseStream = remoteClient.GetStream();
            Stream remoteStream = baseStream;

            if (app.UseTls)
            {
                var sslStream = new System.Net.Security.SslStream(
                    baseStream,
                    leaveInnerStreamOpen: false,
                    new System.Net.Security.RemoteCertificateValidationCallback((sender, certificate, chain, sslPolicyErrors) =>
                    {
                        if (!app.ValidateCertificate)
                        {
                            return true; // Unconditionally accept self-signed certificates
                        }
                        return sslPolicyErrors == System.Net.Security.SslPolicyErrors.None;
                    })
                );

                await sslStream.AuthenticateAsClientAsync(app.Host).ConfigureAwait(false);
                remoteStream = sslStream;
            }

            await using (remoteStream)
            {
                Console.WriteLine($"[Proxy] Tunnel established: C64 <-> RIFT Gate <-> {app.Host}:{app.Port} {(app.UseTls ? "(TLS Encrypted)" : "(Unencrypted)")}");

                // The C64 client only sends the 'READY' handshake once upon boot/initial gateway connection.
                // Since proxy target apps expect to receive this handshake on connection, RiftGate sends
                // 'READY\r' on behalf of the client immediately after the TCP tunnel is established.
                var readyHandshake = Encoding.ASCII.GetBytes("READY\r");
                await remoteStream.WriteAsync(readyHandshake, connectCts.Token).ConfigureAwait(false);
                await remoteStream.FlushAsync(connectCts.Token).ConfigureAwait(false);

                using var relayCts = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellationToken);
                var clientToRemoteTask = ForwardClientToRemoteAsync(connection, remoteStream, relayCts.Token);
                var remoteToClientTask = ForwardRemoteToClientAsync(remoteStream, connection, relayCts.Token);

                var completedTask = await Task.WhenAny(clientToRemoteTask, remoteToClientTask).ConfigureAwait(false);

                await relayCts.CancelAsync().ConfigureAwait(false);

                try
                {
                    await Task.WhenAll(clientToRemoteTask, remoteToClientTask).ConfigureAwait(false);
                }
                catch (Exception) { }

                Console.WriteLine($"[Proxy] Tunnel closed for {app.Name}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Proxy] Error with {app.Name}: {ex.Message}");

            await client.ClearScreenAsync(sessionCancellationToken).ConfigureAwait(false);
            await client.WriteAtAsync(0, 10, "========================================", Rift64Color.Red, sessionCancellationToken).ConfigureAwait(false);
            await client.WriteAtAsync(0, 11, "Connection failed!", Rift64Color.Red, sessionCancellationToken).ConfigureAwait(false);
            await client.WriteAtAsync(0, 12, $"{ex.Message}", Rift64Color.White, sessionCancellationToken).ConfigureAwait(false);
            await client.WriteAtAsync(0, 14, "Press any key to return.", Rift64Color.Yellow, sessionCancellationToken).ConfigureAwait(false);
            await client.WriteAtAsync(0, 15, "========================================", Rift64Color.Red, sessionCancellationToken).ConfigureAwait(false);

            try
            {
                await client.ReadKeyAsync(TimeSpan.FromSeconds(8), sessionCancellationToken).ConfigureAwait(false);
            }
            catch { }
            return;
        }
    }

    private static async Task ForwardClientToRemoteAsync(
        IClientConnection source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        while (!cancellationToken.IsCancellationRequested)
        {
            int bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead <= 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ForwardRemoteToClientAsync(
        Stream source,
        IClientConnection destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        while (!cancellationToken.IsCancellationRequested)
        {
            int bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead <= 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
        }
    }

    private static List<RegisteredApp> LoadApps(string csvPath)
    {
        var list = new List<RegisteredApp>();

        if (!File.Exists(csvPath))
        {
            try
            {
                File.WriteAllText(csvPath, GetDefaultAppsCsv());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not create default apps.csv: {ex.Message}");
            }
        }

        if (File.Exists(csvPath))
        {
            try
            {
                var lines = File.ReadAllLines(csvPath);
                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                    {
                        continue;
                    }

                    var parts = ParseCsvLine(line);
                    if (parts.Length >= 4)
                    {
                        if (int.TryParse(parts[1], out int port))
                        {
                            bool useTls = parts.Length >= 5 && (parts[4] == "1" || string.Equals(parts[4], "true", StringComparison.OrdinalIgnoreCase));
                            bool validateCert = parts.Length >= 6 && (parts[5] == "1" || string.Equals(parts[5], "true", StringComparison.OrdinalIgnoreCase));

                            list.Add(new RegisteredApp
                            {
                                Host = parts[0],
                                Port = port,
                                Name = parts[2],
                                Description = parts[3],
                                UseTls = useTls,
                                ValidateCertificate = validateCert
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading apps.csv: {ex.Message}");
            }
        }

        return list;
    }

    private static List<RegisteredApp> FilterApps(List<RegisteredApp> apps, string searchQuery)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return new List<RegisteredApp>(apps);
        }

        var keywords = searchQuery.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return apps.Where(app =>
            keywords.All(kw =>
                app.Name.ToLower().Contains(kw) ||
                app.Description.ToLower().Contains(kw)
            )
        ).ToList();
    }

    private static string GetDefaultAppsCsv() => """
# Rift Gate Registered Apps database
# Format: dns/ip, port, name, description, useTls, validateCert
127.0.0.1, 8001, "Rift Writer", "An 80-column PETSCII text editor for your Commodore 64. Edit documents with formatting!", 0, 0
127.0.0.1, 64080, "Rift SDK Demo", "The RiftServe64 SDK Interactive Menu with sprite, split raster, and audio players.", 0, 0
127.0.0.1, 64443, "Rift SDK Demo (Secure)", "The RiftServe64 SDK Interactive Menu encrypted via SSL/TLS over the Internet.", 1, 0
""";

    private static string ResolveWorkingFilePath(string fileName)
    {
        var currentDirectoryPath = Path.Combine(Environment.CurrentDirectory, fileName);
        if (File.Exists(currentDirectoryPath))
        {
            return currentDirectoryPath;
        }

        var baseDirectoryPath = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(baseDirectoryPath))
        {
            return baseDirectoryPath;
        }

        return currentDirectoryPath;
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString().Trim());
        return result.ToArray();
    }

    private static List<string> WordWrap(string text, int width)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return lines;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var currentLine = "";

        foreach (var word in words)
        {
            if (currentLine.Length == 0)
            {
                currentLine = word;
            }
            else if (currentLine.Length + 1 + word.Length <= width)
            {
                currentLine += " " + word;
            }
            else
            {
                lines.Add(currentLine);
                currentLine = word;
            }
        }

        if (currentLine.Length > 0)
        {
            lines.Add(currentLine);
        }

        return lines;
    }

    private static async Task RenderMenuRowAsync(
        Rift64ProtocolClient client,
        byte row,
        int appIdx,
        RegisteredApp app,
        bool isSelected,
        CancellationToken cancellationToken)
    {
        string marker = isSelected ? "*" : " ";
        Rift64Color color = isSelected ? Rift64Color.Yellow : Rift64Color.LightGray;

        string namePart = app.Name.Length > 26 ? app.Name.Substring(0, 26) : app.Name.PadRight(26);
        string linePrefix = $"{marker} {(appIdx + 1):D2}. {namePart}";
        
        // Write the prefix (columns 2..33)
        await client.WriteAtAsync(2, row, linePrefix, color, cancellationToken).ConfigureAwait(false);

        // Get security block color
        Rift64Color blockColor;
        if (!app.UseTls)
        {
            blockColor = Rift64Color.Red; // Red for unencrypted
        }
        else if (app.ValidateCertificate)
        {
            blockColor = Rift64Color.LightGreen; // Green for CA Validated TLS
        }
        else
        {
            blockColor = Rift64Color.Yellow; // Yellow for Self-Signed TLS
        }

        // Draw the security badge "[<ball>]" at columns 34..36.
        // The T (colored text) command masks the high bit and only remaps A-Z, so
        // it cannot emit these glyphs faithfully. Poke screen codes directly into
        // screen RAM instead. PETSCII 91/209/93 ('[', ball, ']') map to screen
        // codes 27/81/29; colours go to the matching color RAM cells.
        int badgeOffset = row * 40 + 34;
        byte[] badgeChars = { 27, 81, 29 };
        byte[] badgeColors = { (byte)color, (byte)blockColor, (byte)color };
        await client.StoreMemoryAsync((ushort)(0x0400 + badgeOffset), badgeChars, cancellationToken).ConfigureAwait(false);
        await client.StoreMemoryAsync((ushort)(0xD800 + badgeOffset), badgeColors, cancellationToken).ConfigureAwait(false);
    }
}
