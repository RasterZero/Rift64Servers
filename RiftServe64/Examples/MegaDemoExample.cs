using RiftServe64.Sdk.Protocol;

public record SpriteTextBlock(ushort Delay, string Line1, string Line2);
public record SpriteTextBatch(byte[][] Sprites, ushort Delay);

/// <summary>
/// A full-feature showcase that sequences VIC-II bitmap graphics, SID music and
/// hardware sprites into a single demo, exercising most of the RIFT64 protocol.
/// <para>
/// It runs the VIC in bank 3 ($C000-$FFFF) so multicolour/hi-res bitmaps live at
/// $E000 with the colour matrix at $C400 and Color RAM at $D800, while SID music
/// plays from a tracker song at $7000. Sprite patterns are uploaded to $C800 with
/// pointers at $C7F8, and per-frame motion is sent as batched
/// <see cref="Rift64SpriteConfig"/> packets. Every memory target sits in the safe
/// $4000-$FFFF zone (under the KERNAL ROM shadow), never the client program at
/// $0801-$37FF, and a guaranteed restore returns the VIC to bank-0 text mode.
/// </para>
/// </summary>
public sealed class MegaDemoExample : IRift64MenuExample
{
    public char Key => 'D';
    public string MenuLabel => "Mega Demo";

    public async Task RunAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        try
        {
            await RunCoreAsync(client, initialIdentity, cancellationToken);
        }
        finally
        {
            await RestoreClientStateAsync(client);
        }
    }

    // Phase 2.3: best-effort restoration that silences the SID, disables every
    // sprite and returns the VIC to the standard bank-0 text display, so the host
    // menu renders correctly even if this multi-stage demo faults or is cancelled.
    private static async Task RestoreClientStateAsync(Rift64ProtocolClient client)
    {
        try
        {
            await client.StopSongAsync(CancellationToken.None);
            for (byte i = 0; i < 8; i++)
            {
                await client.SetSpriteAsync(i, 0, 0, Rift64Color.Black, 0, VicBank.Bank3, false, CancellationToken.None);
            }
            await client.SetDisplayModeAsync(
                Rift64DisplayMode.Text,
                vicBank: VicBank.Bank0,
                d018: 0x14, d011: 0x1B, d016: 0x08,
                border: Rift64Color.Black, background: Rift64Color.Black,
                CancellationToken.None);
        }
        catch
        {
            // The connection may already be gone during shutdown; ignore.
        }
    }

    private async Task RunCoreAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 13: RIFT64 MEGA DEMO", Rift64Color.LightGreen, cancellationToken);

        // 1. Locate all package files
        string? pkgDir = ExampleAssets.FindDirectory("pkg");
        if (pkgDir == null)
        {
            await client.WriteAtAsync(0, 2, "ERROR: pkg/ DIRECTORY NOT FOUND!", Rift64Color.Red, cancellationToken);
            await client.WriteAtAsync(0, 4, "PRESS ANY KEY TO RETURN.", Rift64Color.Yellow, cancellationToken);
            await client.PauseForKeyAsync(cancellationToken: cancellationToken);
            return;
        }

        // Paths
        string dsiPath = Path.Combine(pkgDir, "DSI.pkg");
        string introPath = Path.Combine(pkgDir, "intro_text.pkg");
        string wizPath = Path.Combine(pkgDir, "Wizard.pkg");
        
        // Validate files existence
        if (!File.Exists(dsiPath) || !File.Exists(introPath) || !File.Exists(wizPath))
        {
            await client.WriteAtAsync(0, 2, "ERROR: MISSING DEMO .pkg FILES IN pkg/!", Rift64Color.Red, cancellationToken);
            await client.WriteAtAsync(0, 4, "PRESS ANY KEY TO RETURN.", Rift64Color.Yellow, cancellationToken);
            await client.PauseForKeyAsync(cancellationToken: cancellationToken);
            return;
        }

        await client.WriteAtAsync(0, 2, "ALL ASSETS LOCATED! PREPARING... ", Rift64Color.White, cancellationToken);
        await Task.Delay(800, cancellationToken);

        // ==========================================
        // STEP 1: BLACK SCREEN
        // ==========================================
        await client.WriteAtAsync(0, 4, "1. FORCING BLACK SCREEN...", Rift64Color.White, cancellationToken);
        await client.SetColorsAsync(Rift64Color.Black, Rift64Color.Black, cancellationToken);
        await client.ClearScreenAsync(cancellationToken);
        // Disable all sprites (0..7). Firmware is still in text mode (bank 0), and
        // the Y command always reapplies the VIC bank — pass Bank0 to avoid
        // briefly switching the VIC to bank 3 before $C400 has been uploaded.
        for (byte i = 0; i < 8; i++)
        {
            await client.SetSpriteAsync(i, 0, 0, Rift64Color.Black, 0, VicBank.Bank0, false, cancellationToken);
        }
        await Task.Delay(500, cancellationToken);

        // ==========================================
        // STEP 2: LOAD AND DISPLAY DEMO BITMAP
        // ==========================================
        var dsiBytes = await File.ReadAllBytesAsync(dsiPath, cancellationToken);
        var dsiPkg = ParseCbmp(dsiBytes);

        // Upload DSI Bitmap (8000 bytes at $E000)
        await client.StoreMemoryLargeCheckedAsync(0xE000, dsiPkg.Bitmap, cancellationToken: cancellationToken);
        // Upload Screen (1000 bytes at $C400)
        await client.StoreMemoryLargeCheckedAsync(0xC400, dsiPkg.Screen, cancellationToken: cancellationToken);
        // Upload Color RAM if multicolor
        if (dsiPkg.IsMulticolor)
        {
            await client.StoreMemoryLargeCheckedAsync(0xD800, dsiPkg.Color, cancellationToken: cancellationToken);
        }

        // d018=0x18 selects screen slot 1 ($C400) + bitmap base $2000 within VIC
        // bank 3 ($C000-$FFFF). d011=0x3B turns on bitmap mode (bit 5) plus 25-row
        // + display-enable; d016=0x18 enables multicolor (bit 4) + 38-col mode,
        // $08 is hires.
        await client.SetDisplayModeAsync(
            dsiPkg.IsMulticolor ? Rift64DisplayMode.MulticolorBitmap : Rift64DisplayMode.HiresBitmap,
            vicBank: VicBank.Bank3,
            d018: 0x18,
            d011: 0x3B,
            d016: dsiPkg.IsMulticolor ? (byte)0x18 : (byte)0x08,
            border: (Rift64Color)dsiPkg.Border,
            background: (Rift64Color)dsiPkg.Bg,
            cancellationToken);

        // ==========================================
        // STEP 3: START TRACKER MUSIC PLAYBACK
        // ==========================================
        // Two-voice tracker song at $7000. No drums here: the demo's bitmap
        // lives in VIC bank 3 ($C000+), which is the default SFX bank page.
        await client.StopSongAsync(cancellationToken);
        await client.SoundBridgeDefineInstrumentAsync(1, pulseWidth: 0x0000, attackDecay: 0x09, sustainRelease: 0x66, control: SidWaveform.Sawtooth | SidWaveform.Gate, cancellationToken);
        await client.SoundBridgeDefineInstrumentAsync(2, pulseWidth: 0x0600, attackDecay: 0x29, sustainRelease: 0x97, control: SidWaveform.Pulse | SidWaveform.Gate, cancellationToken);
        await client.UploadSongAsync(Rift64ProtocolClient.TrackerSongAddress, BuildDemoSong(), cancellationToken);
        await client.SetAudioVolumeAsync(15, cancellationToken);
        await client.PlaySongAsync(0, cancellationToken);

        // ==========================================
        // STEP 4: INTRO SPRITE TEXT
        // ==========================================
        var introBytes = await File.ReadAllBytesAsync(introPath, cancellationToken);
        var batches = ParseCspk(introBytes);

        const ushort SPRITE_BANK3_BASE = 0xC800;
        const byte SPRITE_PTR_BASE = 0x20;
        const byte SPRITE_Y_TARGET = 229;
        const byte SPRITE_START_X = 88;

        foreach (var batch in batches)
        {
            // Upload sprite data (8 blocks of 64 bytes)
            for (byte i = 0; i < 8; i++)
            {
                ushort addr = (ushort)(SPRITE_BANK3_BASE + i * 64);
                await client.StoreMemoryLargeCheckedAsync(addr, batch.Sprites[i], cancellationToken: cancellationToken);
            }

            // Write sprite pointers to bank-3 pointer area ($C7F8)
            var ptrData = new byte[8];
            for (byte i = 0; i < 8; i++)
            {
                ptrData[i] = (byte)(SPRITE_PTR_BASE + i);
            }
            await client.StoreMemoryLargeCheckedAsync(0xC7F8, ptrData, cancellationToken: cancellationToken);

            // Bouncing Physics Drop Animation
            double y = 30.0;
            double v = 0.0;
            double gravity = 3.2;
            double restitution = 0.35;
            double targetY = (double)SPRITE_Y_TARGET;
            bool active = true;

            // Set initial positions with white color (1) and priority: false (in front of background pixels)
            for (byte i = 0; i < 8; i++)
            {
                ushort x = (ushort)(SPRITE_START_X + i * 24);
                byte ptr = (byte)(SPRITE_PTR_BASE + i);
                await client.SetSpriteMulticolorAsync(
                    spriteId: i,
                    x: x,
                    y: (byte)y,
                    color: Rift64Color.White,
                    pointer: ptr,
                    bank: VicBank.Bank3,
                    enabled: true,
                    multicolor: false,
                    expandX: false,
                    expandY: false,
                    priority: false,
                    cancellationToken: cancellationToken);
            }

            while (active && !cancellationToken.IsCancellationRequested)
            {
                v += gravity;
                y += v;
                if (y >= targetY)
                {
                    y = targetY;
                    v = -v * restitution;
                    if (Math.Abs(v) < 1.5)
                    {
                        y = targetY;
                        v = 0.0;
                        active = false; // settled
                    }
                }

                var positions = new Dictionary<byte, (int X, byte Y)>();
                for (byte i = 0; i < 8; i++)
                {
                    positions[i] = (SPRITE_START_X + i * 24, (byte)y);
                }
                await client.SetSpritePositionsAsync(positions, cancellationToken);
                await Task.Delay(40, cancellationToken); // ~25 fps
            }

            // Lock positions firmly at targetY
            var finalPositions = new Dictionary<byte, (int X, byte Y)>();
            for (byte i = 0; i < 8; i++)
            {
                finalPositions[i] = (SPRITE_START_X + i * 24, SPRITE_Y_TARGET);
            }
            await client.SetSpritePositionsAsync(finalPositions, cancellationToken);

            // Hold for delay
            await Task.Delay(batch.Delay, cancellationToken);

            // Fade out
            var fadeOutColors = new[] { Rift64Color.White, Rift64Color.LightGray, Rift64Color.MediumGray, Rift64Color.DarkGray, Rift64Color.Black };
            foreach (var col in fadeOutColors)
            {
                for (byte i = 0; i < 8; i++)
                {
                    ushort x = (ushort)(SPRITE_START_X + i * 24);
                    byte ptr = (byte)(SPRITE_PTR_BASE + i);
                    await client.SetSpriteMulticolorAsync(
                        spriteId: i,
                        x: x,
                        y: SPRITE_Y_TARGET,
                        color: col,
                        pointer: ptr,
                        bank: VicBank.Bank3,
                        enabled: true,
                        multicolor: false,
                        expandX: false,
                        expandY: false,
                        priority: false,
                        cancellationToken: cancellationToken);
                }
                await Task.Delay(150, cancellationToken);
            }
        }

        // Disable all sprites
        for (byte i = 0; i < 8; i++)
        {
            await client.SetSpriteAsync(i, 0, 0, Rift64Color.Black, 0, VicBank.Bank3, false, cancellationToken);
        }

        // ==========================================
        // STEP 5: LOAD AND DISPLAY WIZARD BITMAP
        // ==========================================
        var wizBytes = await File.ReadAllBytesAsync(wizPath, cancellationToken);
        var wizPkg = ParseCbmp(wizBytes);

        // Force black screen before showing Wizard to hide the transition
        await client.SetColorsAsync(Rift64Color.Black, Rift64Color.Black, cancellationToken);
        await client.ClearScreenAsync(cancellationToken);

        // Upload Wizard Bitmap (8000 bytes at $E000)
        await client.StoreMemoryLargeCheckedAsync(0xE000, wizPkg.Bitmap, cancellationToken: cancellationToken);
        // Upload Screen (1000 bytes at $C400)
        await client.StoreMemoryLargeCheckedAsync(0xC400, wizPkg.Screen, cancellationToken: cancellationToken);
        // Upload Color RAM if multicolor
        if (wizPkg.IsMulticolor)
        {
            await client.StoreMemoryLargeCheckedAsync(0xD800, wizPkg.Color, cancellationToken: cancellationToken);
        }

        // Switch to multicolor/hires mode (VIC bank 3 — screen $C400, bitmap $E000)
        await client.SetDisplayModeAsync(
            wizPkg.IsMulticolor ? Rift64DisplayMode.MulticolorBitmap : Rift64DisplayMode.HiresBitmap,
            vicBank: VicBank.Bank3,
            d018: 0x18,
            d011: 0x3B,
            d016: wizPkg.IsMulticolor ? (byte)0x18 : (byte)0x08,
            border: (Rift64Color)wizPkg.Border,
            background: (Rift64Color)wizPkg.Bg,
            cancellationToken);

        // Wait 3 seconds
        await Task.Delay(3000, cancellationToken);

        // ==========================================
        // STEP 6: REAL-TIME LERP BALLS ANIMATION
        // ==========================================
        var balls = GenerateBallSprites();

        // Upload ball sprite patterns to $C800 (VIC bank 3 offset)
        for (byte i = 0; i < 8; i++)
        {
            ushort addr = (ushort)(0xC800 + i * 64);
            await client.StoreMemoryLargeCheckedAsync(addr, balls[i], cancellationToken: cancellationToken);
        }

        // Write sprite pointers to bank-3 pointer area ($C7F8)
        var ballPtrData = new byte[8];
        for (byte i = 0; i < 8; i++)
        {
            ballPtrData[i] = (byte)(0x20 + i);
        }
        await client.StoreMemoryLargeCheckedAsync(0xC7F8, ballPtrData, cancellationToken: cancellationToken);

        // Enable all 8 sprites
        var currentPositions = new (double X, double Y)[8];
        for (byte i = 0; i < 8; i++)
        {
            currentPositions[i] = (24 + i * 40, 150.0);
            await client.SetSpriteMulticolorAsync(
                spriteId: i,
                x: (int)currentPositions[i].X,
                y: (byte)currentPositions[i].Y,
                color: (byte)(i + 1),
                pointer: (byte)(0x20 + i),
                bank: VicBank.Bank3,
                enabled: true,
                multicolor: false,
                expandX: false,
                expandY: false,
                priority: false,
                cancellationToken: cancellationToken);
        }

        var startTime = DateTime.UtcNow;
        var lastSendTime = DateTime.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - startTime).TotalSeconds;

            if ((now - lastSendTime).TotalMilliseconds >= 80)
            {
                lastSendTime = now;

                // Cycle colors & positions
                var spritesUpdate = ComputeSpriteFrame(elapsed, currentPositions);

                var spriteConfigs = new List<Rift64SpriteConfig>();
                for (byte i = 0; i < 8; i++)
                {
                    var spr = spritesUpdate[i];
                    spriteConfigs.Add(new Rift64SpriteConfig(
                        Id: i,
                        X: spr.X,
                        Y: spr.Y,
                        Color: spr.Color,
                        Pointer: spr.Pointer,
                        Bank: VicBank.Bank3,
                        Enabled: true
                    ));
                }

                // Send all 8 configs in a single high-performance batch write!
                await client.SetSpriteConfigAsync(spriteConfigs, cancellationToken);
            }

            // Keyboard Exit Check: read raw bytes to detect any keypress on the C64
            // Only read if data is actually available on the socket, avoiding blocking!
            if (client.IsDataAvailable)
            {
                var exitBuffer = new byte[16];
                int read = await client.ReadRawAsync(exitBuffer, cancellationToken);
                if (read > 0)
                {
                    // Any key press exits the ball demo loop
                    break;
                }
            }

            await Task.Delay(10, cancellationToken);
        }

        // Cleanup: Stop SID Music
        await client.StopSongAsync(cancellationToken);

        // Disable all sprites
        for (byte i = 0; i < 8; i++)
        {
            await client.SetSpriteAsync(i, 0, 0, Rift64Color.Black, 0, VicBank.Bank3, false, cancellationToken);
        }

        // Restore to standard Text Mode and return to main menu
        await client.ClearScreenAsync(cancellationToken);
        await client.SetDisplayModeAsync(
            Rift64DisplayMode.Text,
            vicBank: VicBank.Bank0,
            d018: 0x14,
            d011: 0x1B,
            d016: 0x08,
            border: Rift64Color.Black,
            background: Rift64Color.Black,
            cancellationToken);
    }

    private record CbmpPackage(bool IsMulticolor, byte Bg, byte Border, byte[] Bitmap, byte[] Screen, byte[] Color);

    // The demo soundtrack: a looping two-voice piece (saw bass, pulse lead).
    private static TrackerSong BuildDemoSong()
    {
        var song = new TrackerSong(rowsPerPattern: 32) { Speed = 6, LoopOrder = 0 };
        var p = song.AddPattern();

        p.SetNote(0, 0, "A-2", 1).SetNote(4, 0, "A-2", 1).SetNote(8, 0, "F-2", 1).SetNote(12, 0, "F-2", 1)
         .SetNote(16, 0, "C-2", 1).SetNote(20, 0, "C-2", 1).SetNote(24, 0, "G-2", 1).SetNote(28, 0, "G-2", 1);
        p.SetNote(0, 1, "A-4", 2).SetNote(6, 1, "E-4", 2).SetNote(8, 1, "F-4", 2).SetNote(14, 1, "C-5", 2)
         .SetNote(16, 1, "E-4", 2).SetNote(22, 1, "G-4", 2).SetNote(24, 1, "B-4", 2).SetNote(30, 1, "D-5", 2);

        song.Order.Add(0);
        return song;
    }

    private static CbmpPackage ParseCbmp(byte[] data)
    {
        bool isMulticolor = true;
        byte bg = 0;
        byte border = 0;
        int offset = 0;

        if (data.Length == 10008)
        {
            var magic = System.Text.Encoding.ASCII.GetString(data, 0, 4);
            if (magic == "CBMP")
            {
                isMulticolor = data[5] == 1;
                bg = data[6];
                border = data[7];
                offset = 8;
            }
        }
        else if (data.Length != 10000)
        {
            throw new InvalidDataException("Unexpected CBMP file size");
        }

        var bitmap = new byte[8000];
        var screen = new byte[1000];
        var color = new byte[1000];

        Array.Copy(data, offset, bitmap, 0, 8000);
        Array.Copy(data, offset + 8000, screen, 0, 1000);
        Array.Copy(data, offset + 9000, color, 0, 1000);

        return new CbmpPackage(isMulticolor, bg, border, bitmap, screen, color);
    }

    private static List<SpriteTextBatch> ParseCspk(byte[] data)
    {
        var magic = System.Text.Encoding.ASCII.GetString(data, 0, 4);
        if (magic != "CSPK") throw new InvalidDataException("Not a CSPK package");

        byte version = data[4];
        if (version != 1) throw new InvalidDataException("Unsupported CSPK version");

        byte count = data[6];
        int off = 7;

        var blocks = new List<SpriteTextBlock>();
        for (int i = 0; i < count; i++)
        {
            ushort delay = (ushort)(data[off] | (data[off + 1] << 8));
            off += 2;

            byte l1Len = data[off++];
            string line1 = System.Text.Encoding.ASCII.GetString(data, off, l1Len);
            off += l1Len;

            byte l2Len = data[off++];
            string line2 = System.Text.Encoding.ASCII.GetString(data, off, l2Len);
            off += l2Len;

            blocks.Add(new SpriteTextBlock(delay, line1, line2));
        }

        var batches = new List<SpriteTextBatch>();
        for (int i = 0; i < count; i++)
        {
            var sprites = new byte[8][];
            for (int s = 0; s < 8; s++)
            {
                sprites[s] = new byte[64];
                Array.Copy(data, off, sprites[s], 0, 64);
                off += 64;
            }
            batches.Add(new SpriteTextBatch(sprites, blocks[i].Delay));
        }

        return batches;
    }

    private record ComputedSprite(byte Id, int X, byte Y, byte Color, byte Pointer);

    private static List<ComputedSprite> ComputeSpriteFrame(double t, (double X, double Y)[] currentPositions)
    {
        const double PATTERN_SECS = 6.0;
        const double FADE_SECS = 1.0;
        int patternIndex = (int)Math.Floor(t / PATTERN_SECS) % 7;
        double patternT = t % PATTERN_SECS;

        double blend = Math.Min(1.0, patternT / FADE_SECS);

        var GREYS = new byte[] { 11, 12, 15, 1, 15, 12 };
        var BLUES = new byte[] { 6, 14, 3, 1, 3, 14 };

        var sprites = new List<ComputedSprite>();
        for (byte i = 0; i < 8; i++)
        {
            double fi = i / 8.0;
            var target = PatternPosition(patternIndex, i, fi, t);

            double colorPhase = (t * 1.5 + i * 0.8) % GREYS.Length;
            int colorIdx = (int)Math.Floor(colorPhase);
            byte color = (byte)((i % 2 == 0) ? GREYS[colorIdx] : BLUES[colorIdx]);

            var cur = currentPositions[i];
            double lerpFactor = 0.25 + blend * 0.15;
            double nx = cur.X + (target.X - cur.X) * lerpFactor;
            double ny = cur.Y + (target.Y - cur.Y) * lerpFactor;
            currentPositions[i] = (nx, ny);

            sprites.Add(new ComputedSprite(
                Id: i,
                X: (int)Math.Round(Math.Max(24.0, Math.Min(343.0, nx))),
                Y: (byte)Math.Round(Math.Max(50.0, Math.Min(249.0, ny))),
                Color: color,
                Pointer: (byte)(0x20 + i)
            ));
        }
        return sprites;
    }

    private static (double X, double Y) PatternPosition(int pattern, byte i, double fi, double t)
    {
        const double CX = 184.0, CY = 150.0;
        double x, y;

        if (pattern == 0)
        {
            double phase = (i < 4) ? 0.0 : Math.PI;
            x = 60.0 + i * 35.0;
            y = CY + Math.Sin(t * 2.0 + fi * Math.PI * 4.0 + phase) * 80.0;
        }
        else if (pattern == 1)
        {
            double a = t * 0.8 + fi * Math.PI * 2.0;
            x = CX + Math.Sin(a) * 120.0;
            y = CY + Math.Sin(a * 2.0) * 70.0;
        }
        else if (pattern == 2)
        {
            double angle = fi * Math.PI * 2.0 + t * 0.6;
            double r = 40.0 + Math.Sin(t * 0.5) * 50.0 + 30.0;
            x = CX + Math.Cos(angle) * r;
            y = CY + Math.Sin(angle) * r;
        }
        else if (pattern == 3)
        {
            int row = i / 2;
            int col = i % 2;
            x = CX + Math.Sin(t * 1.5 + row * 1.2) * (100.0 - row * 15.0) + (col != 0 ? 30.0 : -30.0);
            y = 70.0 + row * 50.0 + Math.Cos(t * 2.0 + col * Math.PI) * 15.0;
        }
        else if (pattern == 4)
        {
            double R = 80.0, rr = 30.0;
            double a = t * 0.7 + fi * Math.PI * 2.0;
            x = CX + (R - rr) * Math.Cos(a) + rr * Math.Cos((R - rr) / rr * a);
            y = CY + (R - rr) * Math.Sin(a) - rr * Math.Sin((R - rr) / rr * a);
        }
        else if (pattern == 5)
        {
            double freq = 1.0 + i * 0.12;
            x = CX + Math.Sin(t * freq) * (110.0 - i * 8.0);
            y = 60.0 + i * 25.0;
        }
        else
        {
            double angle = t * 0.8 + fi * Math.PI * 2.0;
            double arm = t * 0.3 + fi * 6.0;
            double r = 20.0 + (arm % 3.0) * 35.0;
            x = CX + Math.Cos(angle + Math.Log(r + 1.0) * 0.5) * r;
            y = CY + Math.Sin(angle + Math.Log(r + 1.0) * 0.5) * r;
        }

        return (x, y);
    }

    private static List<byte[]> GenerateBallSprites()
    {
        var sprites = new List<byte[]>();
        const int cx = 12, cy = 10;
        for (int i = 0; i < 8; i++)
        {
            double r = 9.0 - i * 0.4;
            var block = new byte[64];
            for (int row = 0; row < 21; row++)
            {
                for (int b = 0; b < 3; b++)
                {
                    byte bval = 0;
                    for (int bit = 0; bit < 8; bit++)
                    {
                        int x = b * 8 + bit;
                        int dx = x - cx;
                        int dy = row - cy;
                        if (dx * dx + dy * dy < r * r)
                        {
                            bval |= (byte)(1 << (7 - bit));
                        }
                    }
                    block[row * 3 + b] = bval;
                }
            }
            sprites.Add(block);
        }
        return sprites;
    }
}
