using RiftServe64.Sdk.Protocol;

public sealed class Rift64InteractiveMenuHost
{
    private readonly Rift64ProtocolClient _client;
    private readonly IReadOnlyDictionary<char, IRift64MenuExample> _examples;

    public Rift64InteractiveMenuHost(Rift64ProtocolClient client)
    {
        _client = client;
        _examples = new Dictionary<char, IRift64MenuExample>
        {
            ['1'] = new ConnectionBannerExample(),
            ['2'] = new ColorRampExample(),
            ['3'] = new PositionGridExample(),
            ['4'] = new CapabilityRecheckExample(),
            ['5'] = new StateAndCheckedOpsExample(),
            ['6'] = new CursorAndScrollExample(),
            ['7'] = new SpriteDemoExample(),
            ['8'] = new VicDisplayExample(),
            ['9'] = new RasterSplitExample(),
            ['A'] = new AudioPlayerExample(),
            ['B'] = new TelemetryDemoExample(),
            ['C'] = new MetatileDemoExample(),
            ['D'] = new MegaDemoExample(),
            ['E'] = new SnakeGameExample(),
            ['F'] = new TetrisGameExample(),
            ['G'] = new SoundBridgeDemoExample(),
            ['H'] = new SoundBridgeMusicExample(),
            ['I'] = new SoundBridgeFxShowcaseExample(),
            ['J'] = new MemoryCopyExample(),
            ['K'] = new RemoteTrackerExample(),
            ['L'] = new DrumKitExample()
        };
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var identity = await _client.IdentifyClientAsync(cancellationToken);

        if (!identity.IsCompatible)
        {
            await _client.ClearScreenAsync(cancellationToken);
            await _client.SetCursorAsync(0, 0, cancellationToken);
            await _client.WriteTextAsync("Unsupported client. Expected RIFT64 capabilities.", cancellationToken);
            await _client.WriteTextAsync($"Raw capability response: {identity.CapabilitiesRaw}", cancellationToken);
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            await ShowMenuAsync(identity, cancellationToken);

            var key = await _client.ReadKeyAsync(TimeSpan.FromMinutes(5), cancellationToken);
            if (key is null)
            {
                continue;
            }

            if (key is 'q' or 'Q')
            {
                await _client.ClearScreenAsync(cancellationToken);
                await _client.SetCursorAsync(0, 0, cancellationToken);
                await _client.WriteTextAsync("Session closed by host menu.", cancellationToken);
                return;
            }

            await RunSelectedExampleAsync(key.Value, identity, cancellationToken);
        }
    }

    private async Task ShowMenuAsync(Rift64ClientIdentity identity, CancellationToken cancellationToken)
    {
        await _client.ClearScreenAsync(cancellationToken);
        await _client.SetColorsAsync(Rift64Color.Black, Rift64Color.White, cancellationToken);

        await _client.WriteAtAsync(0, 0, "RIFT64 SDK EXAMPLE MENU", Rift64Color.LightBlue, cancellationToken);
        await _client.WriteAtAsync(0, 2, $"Client version: {identity.ClientVersion}", Rift64Color.White, cancellationToken);
        await _client.WriteAtAsync(0, 3, $"Capabilities: {identity.CapabilitiesRaw}", Rift64Color.White, cancellationToken);

        // Two-column layout: fill the left column top-to-bottom, then the right.
        var ordered = _examples
            .OrderBy(static kvp => kvp.Key)
            .Select(static kvp => kvp.Value)
            .ToList();

        const byte startRow = 5;
        const byte rightColumnX = 20;
        int rowsPerColumn = (ordered.Count + 1) / 2;

        for (int i = 0; i < ordered.Count; i++)
        {
            var example = ordered[i];
            bool leftColumn = i < rowsPerColumn;
            byte x = leftColumn ? (byte)0 : rightColumnX;
            byte y = (byte)(startRow + (leftColumn ? i : i - rowsPerColumn));
            await _client.WriteAtAsync(x, y, $"{example.Key}) {example.MenuLabel}", Rift64Color.Cyan, cancellationToken);
        }

        await _client.WriteAtAsync(0, 22, "Press 1-9, A-J to run, Q to exit.", Rift64Color.Yellow, cancellationToken);
    }

    private async Task RunSelectedExampleAsync(char key, Rift64ClientIdentity identity, CancellationToken cancellationToken)
    {
        if (_examples.TryGetValue(key, out var example))
        {
            await example.RunAsync(_client, identity, cancellationToken);
            return;
        }

        await _client.WriteAtAsync(0, 12, $"Unknown key: {key}", Rift64Color.Red, cancellationToken);
        await _client.WriteTextAsync("Press any key to continue.", cancellationToken);
        await _client.ReadKeyAsync(TimeSpan.FromMinutes(1), cancellationToken);
    }
}
