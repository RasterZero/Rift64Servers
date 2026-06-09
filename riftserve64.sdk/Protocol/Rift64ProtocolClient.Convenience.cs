namespace RiftServe64.Sdk.Protocol;

/// <summary>
/// High-level convenience helpers: default-timeout overloads, screen-write
/// shortcuts, audio module loader, and a disposable telemetry session.
/// </summary>
public sealed partial class Rift64ProtocolClient
{
    /// <summary>
    /// Conventional load address for a MiniPlayer2 SID module on the C64.
    /// The MiniPlayer2 build assembles to this address; uploads must match.
    /// </summary>
    public const ushort MiniPlayer2ModuleAddress = 0x7000;

    /// <summary>PETSCII screen code for the half-checkerboard glyph (0x66).</summary>
    public const char PetsciiCheckerboard = (char)0x66;

    // ------------------------------------------------------------------
    // Default-timeout overloads — use DefaultAckTimeout
    // ------------------------------------------------------------------

    /// <inheritdoc cref="StoreMemoryCheckedAsync(ushort,ReadOnlyMemory{byte},TimeSpan,CancellationToken)"/>
    public Task<bool?> StoreMemoryCheckedAsync(
        ushort address,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default) =>
        StoreMemoryCheckedAsync(address, data, DefaultAckTimeout, cancellationToken);

    /// <inheritdoc cref="DrawWindowCheckedAsync(byte,byte,string,TimeSpan,CancellationToken)"/>
    public Task<bool?> DrawWindowCheckedAsync(
        byte width,
        byte height,
        string text,
        CancellationToken cancellationToken = default) =>
        DrawWindowCheckedAsync(width, height, text, DefaultAckTimeout, cancellationToken);

    /// <inheritdoc cref="SendFrameAsync(byte,ReadOnlyMemory{byte},TimeSpan,CancellationToken)"/>
    public Task<bool?> SendFrameAsync(
        byte command,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default) =>
        SendFrameAsync(command, payload, DefaultAckTimeout, cancellationToken);

    /// <inheritdoc cref="SetCharsetBankAsync(VicBank,byte,TimeSpan,CancellationToken)"/>
    public Task<bool?> SetCharsetBankAsync(
        VicBank vicBank,
        byte d018,
        CancellationToken cancellationToken = default) =>
        SetCharsetBankAsync(vicBank, d018, DefaultAckTimeout, cancellationToken);

    /// <inheritdoc cref="SetDisplayModeAsync(Rift64DisplayMode,VicBank,byte,byte,byte,Rift64Color,Rift64Color,TimeSpan,CancellationToken)"/>
    public Task<bool?> SetDisplayModeAsync(
        Rift64DisplayMode mode,
        VicBank vicBank,
        byte d018,
        byte d011,
        byte d016,
        Rift64Color border,
        Rift64Color background,
        CancellationToken cancellationToken = default) =>
        SetDisplayModeAsync(mode, vicBank, d018, d011, d016, border, background, DefaultAckTimeout, cancellationToken);

    /// <inheritdoc cref="SetRasterSplitAsync"/>
    public Task<bool?> SetRasterSplitAsync(
        bool enable,
        byte splitLine,
        byte topD011, byte topD016, byte topD018,
        byte botD011, byte botD016, byte botD018,
        CancellationToken cancellationToken = default) =>
        SetRasterSplitAsync(enable, splitLine, topD011, topD016, topD018, botD011, botD016, botD018, DefaultAckTimeout, cancellationToken);

    public Task<bool?> StopAudioAsync(CancellationToken cancellationToken = default) =>
        StopAudioAsync(DefaultAckTimeout, cancellationToken);

    public Task<bool?> StartAudioAsync(byte subtune, CancellationToken cancellationToken = default) =>
        StartAudioAsync(subtune, DefaultAckTimeout, cancellationToken);

    public Task<bool?> PauseAudioAsync(CancellationToken cancellationToken = default) =>
        PauseAudioAsync(DefaultAckTimeout, cancellationToken);

    public Task<bool?> ResumeAudioAsync(CancellationToken cancellationToken = default) =>
        ResumeAudioAsync(DefaultAckTimeout, cancellationToken);

    public Task<bool?> BindAudioModuleAsync(ushort address, CancellationToken cancellationToken = default) =>
        BindAudioModuleAsync(address, DefaultAckTimeout, cancellationToken);

    public Task<bool?> SetAudioVolumeAsync(byte volume, CancellationToken cancellationToken = default) =>
        SetAudioVolumeAsync(volume, DefaultAckTimeout, cancellationToken);

    public Task<byte?> QueryAudioStateAsync(CancellationToken cancellationToken = default) =>
        QueryAudioStateAsync(DefaultAckTimeout, cancellationToken);

    // --- SoundBridge Convenience Overrides ---
    public Task<bool?> SoundBridgeResetAsync(CancellationToken cancellationToken = default) =>
        SoundBridgeResetAsync(DefaultAckTimeout, cancellationToken);

    public Task<bool?> SoundBridgeSetSfxBaseAsync(byte basePage, CancellationToken cancellationToken = default) =>
        SoundBridgeSetSfxBaseAsync(basePage, DefaultAckTimeout, cancellationToken);

    public Task<bool?> SoundBridgeSetVolumeAsync(byte volume, CancellationToken cancellationToken = default) =>
        SoundBridgeSetVolumeAsync(volume, DefaultAckTimeout, cancellationToken);

    public Task<bool?> SoundBridgeSetModeAsync(byte mode, CancellationToken cancellationToken = default) =>
        SoundBridgeSetModeAsync(mode, DefaultAckTimeout, cancellationToken);

    public Task<bool?> SoundBridgeDefineInstrumentAsync(byte id, ushort pulseWidth, byte attackDecay, byte sustainRelease, byte control, CancellationToken cancellationToken = default) =>
        SoundBridgeDefineInstrumentAsync(id, pulseWidth, attackDecay, sustainRelease, control, DefaultAckTimeout, cancellationToken);

    public Task<bool?> SoundBridgePlaySfxAsync(byte sfxId, byte priority, byte flags, CancellationToken cancellationToken = default) =>
        SoundBridgePlaySfxAsync(sfxId, priority, flags, DefaultAckTimeout, cancellationToken);

    public Task<bool?> SoundBridgeStopSfxAsync(CancellationToken cancellationToken = default) =>
        SoundBridgeStopSfxAsync(DefaultAckTimeout, cancellationToken);

    public Task<bool?> SoundBridgeStopAllAsync(CancellationToken cancellationToken = default) =>
        SoundBridgeStopAllAsync(DefaultAckTimeout, cancellationToken);

    public Task<char?> ReadKeyAsync(CancellationToken cancellationToken = default) =>
        ReadKeyAsync(DefaultAckTimeout, cancellationToken);

    // ------------------------------------------------------------------
    // Cursor + colored text in one shot
    // ------------------------------------------------------------------

    /// <summary>
    /// Move the cursor to (<paramref name="x"/>, <paramref name="y"/>) and
    /// write <paramref name="text"/> in <paramref name="color"/>.
    /// </summary>
    public async Task WriteAtAsync(
        byte x, byte y,
        string text,
        Rift64Color color,
        CancellationToken cancellationToken = default)
    {
        await SetCursorAsync(x, y, cancellationToken).ConfigureAwait(false);
        await WriteColoredTextAsync(color, text, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Move the cursor and write plain text in the current color.</summary>
    public async Task WriteAtAsync(
        byte x, byte y,
        string text,
        CancellationToken cancellationToken = default)
    {
        await SetCursorAsync(x, y, cancellationToken).ConfigureAwait(false);
        await WriteTextAsync(text, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Wait up to <paramref name="timeout"/> (default 1 minute) for any
    /// keypress from the C64 keyboard. Returns the key, or <c>null</c> on
    /// timeout. Convenience wrapper around <see cref="ReadKeyAsync(TimeSpan,CancellationToken)"/>.
    /// </summary>
    public Task<char?> PauseForKeyAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        ReadKeyAsync(timeout ?? TimeSpan.FromMinutes(1), cancellationToken);

    // ------------------------------------------------------------------
    // SID module loader
    // ------------------------------------------------------------------

    /// <summary>
    /// Upload a MiniPlayer2 (.bin) module to <paramref name="address"/> in
    /// chunked, checked frames and bind it to the audio engine. The
    /// destination must be page-aligned (low byte = 0).
    /// </summary>
    /// <returns>True if every upload chunk and the bind ACK succeeded.</returns>
    public async Task<bool> LoadSidModuleAsync(
        ushort address,
        ReadOnlyMemory<byte> moduleBytes,
        CancellationToken cancellationToken = default)
    {
        if ((address & 0xFF) != 0)
        {
            throw new ArgumentException(
                "MiniPlayer2 module address must be page-aligned (low byte 0).",
                nameof(address));
        }
        if (moduleBytes.Length == 0)
        {
            throw new ArgumentException("Module is empty.", nameof(moduleBytes));
        }

        var uploaded = await StoreMemoryLargeCheckedAsync(
            address, moduleBytes, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!uploaded) return false;

        var bound = await BindAudioModuleAsync(address, cancellationToken).ConfigureAwait(false);
        return bound == true;
    }

    /// <summary>
    /// Open a typed telemetry session. Disposing the returned object stops
    /// telemetry on the C64 and drains the wire of any in-flight packets.
    /// </summary>
    public async Task<TelemetrySession> StartTelemetrySessionAsync(
        byte divider,
        TelemetryChannels channels,
        CancellationToken cancellationToken = default)
    {
        var session = new TelemetrySession(this);
        await StartTelemetryAsync(divider, channels, cancellationToken).ConfigureAwait(false);
        return session;
    }
}

/// <summary>
/// Disposable wrapper around an active telemetry stream. Feeds incoming
/// bytes to a <see cref="Rift64TelemetryParser"/> and exposes them via
/// <see cref="Frames"/>; bytes that are not part of a packet (e.g.
/// keyboard input) are surfaced via <see cref="UnsolicitedBytes"/>.
/// </summary>
public sealed class TelemetrySession : IAsyncDisposable
{
    private readonly Rift64ProtocolClient _client;
    private readonly Rift64TelemetryParser _parser = new();
    private bool _disposed;

    public event Action<Rift64TelemetryFrame>? FrameReceived;
    public event Action<byte>? UnsolicitedBytes;

    internal TelemetrySession(Rift64ProtocolClient client)
    {
        _client = client;
        _parser.FrameReceived += f => FrameReceived?.Invoke(f);
    }

    /// <summary>
    /// Read raw bytes from the connection and feed them through the parser.
    /// Call this in your main loop. Returns the number of bytes processed.
    /// </summary>
    public async Task<int> PumpAsync(CancellationToken cancellationToken = default)
    {
        var buffer = new byte[64];
        var read = await _client.ReadRawAsync(buffer, cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < read; i++)
        {
            if (!_parser.Feed(buffer[i]))
            {
                UnsolicitedBytes?.Invoke(buffer[i]);
            }
        }
        return read;
    }

    /// <summary>
    /// Async stream of telemetry frames. Yields a frame whenever the parser
    /// emits one. Stops when <paramref name="cancellationToken"/> fires.
    /// </summary>
    public async IAsyncEnumerable<Rift64TelemetryFrame> Frames(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var queue = new Queue<Rift64TelemetryFrame>();
        void Capture(Rift64TelemetryFrame f) => queue.Enqueue(f);
        FrameReceived += Capture;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await PumpAsync(cancellationToken).ConfigureAwait(false);
                while (queue.Count > 0)
                {
                    yield return queue.Dequeue();
                }
            }
        }
        finally
        {
            FrameReceived -= Capture;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await _client.StopTelemetryAsync().ConfigureAwait(false);
        }
        catch { /* best effort */ }

        // Drain any in-flight bytes that were on the wire before stop.
        var drain = new byte[256];
        for (var attempt = 0; attempt < 8; attempt++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            try
            {
                var n = await _client.ReadRawAsync(drain, cts.Token).ConfigureAwait(false);
                if (n <= 0) break;
            }
            catch (OperationCanceledException) { break; }
        }
    }
}
