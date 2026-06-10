using System.Text.RegularExpressions;
using RiftServe64.Sdk.Networking;

namespace RiftServe64.Sdk.Protocol;

public sealed partial class Rift64ProtocolClient
{
    private const byte CommandText = (byte)'!';
    private const byte CommandColoredText = (byte)'T';
    private const byte CommandColor = (byte)'K';
    private const byte CommandPosition = (byte)'P';
    private const byte CommandClear = (byte)'C';
    private const byte CommandWindow = (byte)'W';
    private const byte CommandColoredWindow = (byte)'V';
    private const byte CommandBorder = (byte)'B';
    private const byte CommandEraseLine = (byte)'E';
    private const byte CommandLengthText = (byte)'L';
    private const byte CommandColorBlock = (byte)'Q';
    private const byte CommandSaveBuffer = (byte)'S';
    private const byte CommandRestoreBuffer = (byte)'R';
    private const byte CommandMemoryStore = (byte)'M';
    private const byte CommandCheckedMemory = (byte)'Z';
    private const byte CommandCheckedWindow = (byte)'X';
    private const byte CommandCapabilities = (byte)'?';
    private const byte CommandFrame = (byte)'~';
    private const byte CommandSpriteSet = (byte)'Y';
    private const byte CommandSpritePosition = (byte)'@';
    private const byte CommandSpriteMulticolor = (byte)'U';
    private const byte CommandScrollRegion = (byte)'G';
    private const byte CommandCursorVisibility = (byte)'H';
    private const byte CommandCharsetBank = (byte)'F';
    private const byte CommandDisplayMode = (byte)'I';
    private const byte CommandRasterSplit = (byte)'N';
    private const byte CommandAudio = (byte)'A';
    private const byte CommandTelemetry = (byte)'J';
    private const byte CommandDrawMetatile = (byte)'D';
    private const byte AckByte = (byte)'A';
    private const byte NakByte = (byte)'N';
    private static readonly TimeSpan CommandAckTimeout = TimeSpan.FromMilliseconds(1000);

    private static readonly Regex VersionRegex = new(@"V(?<version>\d+(?:\.\d+)*)", RegexOptions.Compiled);

    private readonly IClientConnection _connection;
    private readonly IRift64TextConverter _textConverter;
    private readonly Queue<byte> _pendingBytes = new();

    /// <summary>
    /// Fired when a non-ACK/NAK byte is received while waiting for an acknowledgement.
    /// This typically contains telemetry data sent asynchronously by the C64.
    /// </summary>
    public event Action<byte>? UnsolicitedByteReceived;

    /// <summary>
    /// Default timeout used by the parameterless overloads of the
    /// <c>*CheckedAsync</c> family and other ACK-returning helpers.
    /// Defaults to 5 seconds.
    /// </summary>
    public TimeSpan DefaultAckTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public Rift64ProtocolClient(IClientConnection connection, IRift64TextConverter? textConverter = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _textConverter = textConverter ?? PetsciiTextConverter.Default;
    }

    public async Task<Rift64ClientIdentity> IdentifyClientAsync(CancellationToken cancellationToken = default)
    {
        var capabilities = await QueryCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        var version = ExtractClientVersion(capabilities);
        var isCompatible = IsCapabilitiesResponse(capabilities);

        return new Rift64ClientIdentity(capabilities, version, isCompatible);
    }

    public async Task<string> QueryCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        const byte maxResponseLines = 4;

        await SendCommandAsync(CommandCapabilities, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);

        for (byte line = 0; line < maxResponseLines; line++)
        {
            var response = await ReadLineAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            if (IsCapabilitiesResponse(response))
            {
                return response;
            }
        }

        return string.Empty;
    }

    public Task ClearScreenAsync(CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(CommandClear, ReadOnlyMemory<byte>.Empty, cancellationToken);
    }

    public Task SetCursorAsync(byte x, byte y, CancellationToken cancellationToken = default)
    {
        var payload = new byte[4];
        EncodeHexByte(x, payload.AsSpan(0, 2));
        EncodeHexByte(y, payload.AsSpan(2, 2));
        return SendCommandAsync(CommandPosition, payload, cancellationToken);
    }

    public Task SetColorsAsync(byte borderColor, byte textColor, CancellationToken cancellationToken = default)
    {
        var payload = new byte[2]
        {
            EncodeHexNibble((byte)(borderColor & 0x0F)),
            EncodeHexNibble((byte)(textColor & 0x0F))
        };

        return SendCommandAsync(CommandColor, payload, cancellationToken);
    }

    public Task SetColorsAsync(Rift64Color borderColor, Rift64Color textColor, CancellationToken cancellationToken = default)
    {
        return SetColorsAsync((byte)borderColor, (byte)textColor, cancellationToken);
    }

    public Task EraseLineAsync(byte count, CancellationToken cancellationToken = default)
    {
        Span<byte> payload = stackalloc byte[2];
        EncodeHexByte(count, payload);
        return SendCommandAsync(CommandEraseLine, payload.ToArray(), cancellationToken);
    }

    public async Task WriteLengthTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var bytes = _textConverter.Encode(text);
        if (bytes.Length == 0 || bytes.Length > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(text), "Text must encode to 1..256 bytes.");
        }

        var payload = new byte[2 + bytes.Length];
        var encodedLength = bytes.Length == 256 ? (byte)0 : (byte)bytes.Length;
        EncodeHexByte(encodedLength, payload.AsSpan(0, 2));
        bytes.CopyTo(payload, 2);

        await SendCommandAsync(CommandLengthText, payload, cancellationToken).ConfigureAwait(false);
        _ = await WaitForAckAsync(CommandAckTimeout, cancellationToken).ConfigureAwait(false);
    }

    public async Task DrawWindowAsync(byte width, byte height, string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var payload = BuildWindowPayload(width, height, EncodeScreenCodes(text));
        await SendCommandAsync(CommandWindow, payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task DrawColoredWindowAsync(Rift64Color color, byte width, byte height, string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var content = BuildWindowPayload(width, height, EncodeScreenCodes(text));
        var payload = new byte[1 + content.Length];
        payload[0] = EncodeHexNibble((byte)color);
        content.CopyTo(payload, 1);

        await SendCommandAsync(CommandColoredWindow, payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task DrawBorderAsync(byte width, byte height, Rift64BorderGlyphs glyphs, CancellationToken cancellationToken = default)
    {
        var payload = new byte[20];

        EncodeHexByte(width, payload.AsSpan(0, 2));
        EncodeHexByte(height, payload.AsSpan(2, 2));
        EncodeHexByte(glyphs.TopLeft, payload.AsSpan(4, 2));
        EncodeHexByte(glyphs.Top, payload.AsSpan(6, 2));
        EncodeHexByte(glyphs.TopRight, payload.AsSpan(8, 2));
        EncodeHexByte(glyphs.Left, payload.AsSpan(10, 2));
        EncodeHexByte(glyphs.Right, payload.AsSpan(12, 2));
        EncodeHexByte(glyphs.BottomLeft, payload.AsSpan(14, 2));
        EncodeHexByte(glyphs.Bottom, payload.AsSpan(16, 2));
        EncodeHexByte(glyphs.BottomRight, payload.AsSpan(18, 2));

        await SendCommandAsync(CommandBorder, payload, cancellationToken).ConfigureAwait(false);
    }

    public Task FillColorBlockAsync(byte x, byte y, byte width, byte height, Rift64Color color, CancellationToken cancellationToken = default)
    {
        var cells = ProtocolCodec.ClampWidth(width) * ProtocolCodec.ClampHeight(height);
        var colors = new byte[cells];
        Array.Fill(colors, (byte)color);
        return FillColorBlockAsync(x, y, width, height, colors, cancellationToken);
    }

    public async Task FillColorBlockAsync(byte x, byte y, byte width, byte height, ReadOnlyMemory<byte> colors, CancellationToken cancellationToken = default)
    {
        byte effectiveWidth = ProtocolCodec.ClampWidth(width);
        byte effectiveHeight = ProtocolCodec.ClampHeight(height);
        var expectedCells = effectiveWidth * effectiveHeight;
        if (colors.Length != expectedCells)
        {
            throw new ArgumentException($"Color payload must contain exactly {expectedCells} bytes.", nameof(colors));
        }

        var payload = new byte[8 + colors.Length];
        EncodeHexByte(x, payload.AsSpan(0, 2));
        EncodeHexByte(y, payload.AsSpan(2, 2));
        EncodeHexByte(width, payload.AsSpan(4, 2));
        EncodeHexByte(height, payload.AsSpan(6, 2));

        var source = colors.Span;
        for (var i = 0; i < source.Length; i++)
        {
            payload[8 + i] = (byte)(source[i] & 0x0F);
        }

        await SendCommandAsync(CommandColorBlock, payload, cancellationToken).ConfigureAwait(false);
        _ = await WaitForAckAsync(CommandAckTimeout, cancellationToken).ConfigureAwait(false);
    }

    public Task SaveScreenBufferAsync(byte bufferIndex = 0, CancellationToken cancellationToken = default)
    {
        ValidateBufferIndex(bufferIndex);
        var payload = new[] { BufferIndexByte(bufferIndex) };
        return SendCommandAsync(CommandSaveBuffer, payload, cancellationToken);
    }

    public Task RestoreScreenBufferAsync(byte bufferIndex = 0, CancellationToken cancellationToken = default)
    {
        ValidateBufferIndex(bufferIndex);
        var payload = new[] { BufferIndexByte(bufferIndex) };
        return SendCommandAsync(CommandRestoreBuffer, payload, cancellationToken);
    }

    public async Task StoreMemoryAsync(ushort address, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ValidateStoreLength(data.Length, nameof(data));

        var payload = BuildMemoryStorePayload(address, data.Span, includeChecksum: false);
        await SendCommandAsync(CommandMemoryStore, payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool?> StoreMemoryCheckedAsync(ushort address, ReadOnlyMemory<byte> data, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ValidateStoreLength(data.Length, nameof(data));

        var payload = BuildMemoryStorePayload(address, data.Span, includeChecksum: true);
        await SendCommandAsync(CommandCheckedMemory, payload, cancellationToken).ConfigureAwait(false);
        return await WaitForAckAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool?> DrawWindowCheckedAsync(byte width, byte height, string text, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var clampedWidth = ProtocolCodec.ClampWidth(width);
        var clampedHeight = ProtocolCodec.ClampHeight(height);
        var required = clampedWidth * clampedHeight;
        if (required > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Checked window supports a maximum of 255 payload bytes.");
        }

        var content = EncodeScreenCodes(text);
        var payload = BuildCheckedWindowPayload(width, height, required, content);

        await SendCommandAsync(CommandCheckedWindow, payload, cancellationToken).ConfigureAwait(false);
        return await WaitForAckAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteTextAsync(string text, CancellationToken cancellationToken = default)
    {
        var payload = _textConverter.Encode(text + "\r");
        await SendCommandAsync(CommandText, payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteColoredTextAsync(byte color, string text, CancellationToken cancellationToken = default)
    {
        var textBytes = _textConverter.Encode(text + "\r");
        var payload = new byte[textBytes.Length + 1];
        payload[0] = EncodeHexNibble((byte)(color & 0x0F));
        textBytes.CopyTo(payload, 1);

        await SendCommandAsync(CommandColoredText, payload, cancellationToken).ConfigureAwait(false);
    }

    public Task WriteColoredTextAsync(Rift64Color color, string text, CancellationToken cancellationToken = default)
    {
        return WriteColoredTextAsync((byte)color, text, cancellationToken);
    }

    public async Task<bool?> SendFrameAsync(byte command, ReadOnlyMemory<byte> payload, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (payload.Length == 0)
        {
            throw new ArgumentException("The C64 client firmware interprets a frame length of 0 as a 256-byte payload. Please provide at least a 1-byte dummy payload (e.g. [0]) to prevent the client from blocking.", nameof(payload));
        }

        if (payload.Length > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), "Frame payload max is 255 bytes.");
        }

        var length = (byte)payload.Length;

        // Build the raw data for rolling checksum: [command, length, ...payload]
        var raw = new byte[2 + payload.Length];
        raw[0] = command;
        raw[1] = length;
        payload.CopyTo(raw.AsMemory(2));

        var checksum = ProtocolCodec.ComputeRollingChecksum(raw);

        // Packet format: [CommandFrame] + [command] + [length_hex] + [payload] + [checksum_hex]
        var packet = new byte[6 + payload.Length];
        packet[0] = CommandFrame;
        packet[1] = command;
        EncodeHexByte(length, packet.AsSpan(2, 2));
        payload.CopyTo(packet.AsMemory(4));
        EncodeHexByte(checksum, packet.AsSpan(4 + payload.Length, 2));

        await _connection.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
        return await WaitForAckAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    public Task SetSpriteAsync(
        byte spriteId,
        int x,
        byte y,
        byte color,
        byte pointer,
        VicBank bank = VicBank.Bank0,
        bool enabled = true,
        CancellationToken cancellationToken = default)
    {
        var payload = new byte[16];

        EncodeHexByte(spriteId, payload.AsSpan(0, 2));
        EncodeHexByte((byte)((x >> 8) & 0x01), payload.AsSpan(2, 2));
        EncodeHexByte((byte)(x & 0xFF), payload.AsSpan(4, 2));
        EncodeHexByte(y, payload.AsSpan(6, 2));
        EncodeHexByte((byte)(color & 0x0F), payload.AsSpan(8, 2));
        EncodeHexByte(pointer, payload.AsSpan(10, 2));
        EncodeHexByte(bank.CiaPortABits(), payload.AsSpan(12, 2));
        EncodeHexByte((byte)(enabled ? 1 : 0), payload.AsSpan(14, 2));

        return SendCommandAsync(CommandSpriteSet, payload, cancellationToken);
    }

    public Task SetSpriteAsync(
        byte spriteId,
        int x,
        byte y,
        Rift64Color color,
        byte pointer,
        VicBank bank = VicBank.Bank0,
        bool enabled = true,
        CancellationToken cancellationToken = default) =>
        SetSpriteAsync(spriteId, x, y, (byte)color, pointer, bank, enabled, cancellationToken);

    public Task SetSpritePositionsAsync(
        IReadOnlyDictionary<byte, (int X, byte Y)> positions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(positions);

        byte mask = 0;
        var sortedIds = new List<byte>();

        foreach (var id in positions.Keys)
        {
            if (id < 8)
            {
                mask |= (byte)(1 << id);
                sortedIds.Add(id);
            }
        }

        sortedIds.Sort();

        var payload = new byte[2 + sortedIds.Count * 6];
        EncodeHexByte(mask, payload.AsSpan(0, 2));

        for (var i = 0; i < sortedIds.Count; i++)
        {
            var id = sortedIds[i];
            var (x, y) = positions[id];

            var offset = 2 + i * 6;
            EncodeHexByte((byte)((x >> 8) & 0x01), payload.AsSpan(offset, 2));
            EncodeHexByte((byte)(x & 0xFF), payload.AsSpan(offset + 2, 2));
            EncodeHexByte(y, payload.AsSpan(offset + 4, 2));
        }

        return SendCommandAsync(CommandSpritePosition, payload, cancellationToken);
    }

    public Task SetSpriteMulticolorAsync(
        byte spriteId,
        int x,
        byte y,
        byte color,
        byte pointer,
        VicBank bank = VicBank.Bank0,
        bool enabled = true,
        bool multicolor = false,
        bool expandX = false,
        bool expandY = false,
        bool priority = false,
        byte sharedColor0 = 0,
        byte sharedColor1 = 0,
        CancellationToken cancellationToken = default)
    {
        byte flags = 0;
        if (multicolor)
        {
            flags |= 0x01;
        }
        if (expandY)
        {
            flags |= 0x02;
        }
        if (expandX)
        {
            flags |= 0x04;
        }
        if (priority)
        {
            flags |= 0x08;
        }

        var payload = new byte[24];

        EncodeHexByte((byte)(spriteId & 0x07), payload.AsSpan(0, 2));
        EncodeHexByte((byte)((x >> 8) & 0x01), payload.AsSpan(2, 2));
        EncodeHexByte((byte)(x & 0xFF), payload.AsSpan(4, 2));
        EncodeHexByte(y, payload.AsSpan(6, 2));
        EncodeHexByte((byte)(color & 0x0F), payload.AsSpan(8, 2));
        EncodeHexByte(pointer, payload.AsSpan(10, 2));
        EncodeHexByte(bank.CiaPortABits(), payload.AsSpan(12, 2));
        EncodeHexByte((byte)(enabled ? 1 : 0), payload.AsSpan(14, 2));
        EncodeHexByte((byte)(flags & 0x0F), payload.AsSpan(16, 2));
        EncodeHexByte((byte)(sharedColor0 & 0x0F), payload.AsSpan(18, 2));
        EncodeHexByte((byte)(sharedColor1 & 0x0F), payload.AsSpan(20, 2));
        EncodeHexByte(0, payload.AsSpan(22, 2)); // reserved / padding "00"

        return SendCommandAsync(CommandSpriteMulticolor, payload, cancellationToken);
    }

    public Task SetSpriteMulticolorAsync(
        byte spriteId,
        int x,
        byte y,
        Rift64Color color,
        byte pointer,
        VicBank bank = VicBank.Bank0,
        bool enabled = true,
        bool multicolor = false,
        bool expandX = false,
        bool expandY = false,
        bool priority = false,
        Rift64Color sharedColor0 = Rift64Color.Black,
        Rift64Color sharedColor1 = Rift64Color.Black,
        CancellationToken cancellationToken = default) =>
        SetSpriteMulticolorAsync(
            spriteId,
            x,
            y,
            (byte)color,
            pointer,
            bank,
            enabled,
            multicolor,
            expandX,
            expandY,
            priority,
            (byte)sharedColor0,
            (byte)sharedColor1,
            cancellationToken);

    public Task ScrollRegionAsync(
        byte x,
        byte y,
        byte width,
        byte height,
        Rift64ScrollDirection direction,
        CancellationToken cancellationToken = default)
    {
        var payload = new byte[9];

        EncodeHexByte(x, payload.AsSpan(0, 2));
        EncodeHexByte(y, payload.AsSpan(2, 2));
        EncodeHexByte(width, payload.AsSpan(4, 2));
        EncodeHexByte(height, payload.AsSpan(6, 2));
        payload[8] = EncodeHexNibble((byte)direction);

        return SendCommandAsync(CommandScrollRegion, payload, cancellationToken);
    }

    public Task SetCursorVisibilityAsync(
        bool visible,
        CancellationToken cancellationToken = default)
    {
        var payload = new[] { visible ? (byte)'1' : (byte)'0' };
        return SendCommandAsync(CommandCursorVisibility, payload, cancellationToken);
    }

    /// <summary>
    /// CHARSET BANK ('F'): set the VIC bank ($DD00) and $D018 (screen slot +
    /// charset slot). The firmware also relocates its CPU screen-draw base to
    /// match the screen slot encoded in <paramref name="d018"/>, so all text,
    /// cursor, scroll, window, clear, screen save/restore and sprite-pointer
    /// writes follow the relocated screen. Colour RAM is fixed at $D800.
    /// The default ($D018 high nibble = 1, bank 0) keeps the screen at $0400.
    /// </summary>
    public async Task<bool?> SetCharsetBankAsync(
        VicBank vicBank,
        byte d018,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var payload = new byte[3];
        payload[0] = EncodeHexNibble(vicBank.CiaPortABits());
        EncodeHexByte(d018, payload.AsSpan(1, 2));

        await SendCommandAsync(CommandCharsetBank, payload, cancellationToken).ConfigureAwait(false);
        return await WaitForAckAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// DISPLAY MODE ('I'): set mode, VIC bank ($DD00), $D018, $D011, $D016 and
    /// the border/background colours. As with CHARSET BANK, the firmware
    /// relocates its CPU screen-draw base to the screen slot encoded in
    /// <paramref name="d018"/>, so the entire text display (text, cursor,
    /// scroll, window, clear, save/restore, sprite pointers) follows the new
    /// screen location. Colour RAM is fixed at $D800. The default screen slot
    /// (high nibble = 1, bank 0) keeps the screen at $0400.
    /// </summary>
    public async Task<bool?> SetDisplayModeAsync(
        Rift64DisplayMode mode,
        VicBank vicBank,
        byte d018,
        byte d011,
        byte d016,
        byte border,
        byte background,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var payload = new byte[14];

        EncodeHexByte((byte)mode, payload.AsSpan(0, 2));
        EncodeHexByte(vicBank.CiaPortABits(), payload.AsSpan(2, 2));
        EncodeHexByte(d018, payload.AsSpan(4, 2));
        EncodeHexByte(d011, payload.AsSpan(6, 2));
        EncodeHexByte(d016, payload.AsSpan(8, 2));
        EncodeHexByte((byte)(border & 0x0F), payload.AsSpan(10, 2));
        EncodeHexByte((byte)(background & 0x0F), payload.AsSpan(12, 2));

        await SendCommandAsync(CommandDisplayMode, payload, cancellationToken).ConfigureAwait(false);
        return await WaitForAckAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool?> SetDisplayModeAsync(
        Rift64DisplayMode mode,
        VicBank vicBank,
        byte d018,
        byte d011,
        byte d016,
        Rift64Color border,
        Rift64Color background,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        SetDisplayModeAsync(
            mode,
            vicBank,
            d018,
            d011,
            d016,
            (byte)border,
            (byte)background,
            timeout,
            cancellationToken);

    public async Task<bool?> SetRasterSplitAsync(
        bool enable,
        byte splitLine,
        byte topD011,
        byte topD016,
        byte topD018,
        byte botD011,
        byte botD016,
        byte botD018,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var payload = new byte[16];

        EncodeHexByte((byte)(enable ? 1 : 0), payload.AsSpan(0, 2));
        EncodeHexByte(splitLine, payload.AsSpan(2, 2));
        EncodeHexByte(topD011, payload.AsSpan(4, 2));
        EncodeHexByte(topD016, payload.AsSpan(6, 2));
        EncodeHexByte(topD018, payload.AsSpan(8, 2));
        EncodeHexByte(botD011, payload.AsSpan(10, 2));
        EncodeHexByte(botD016, payload.AsSpan(12, 2));
        EncodeHexByte(botD018, payload.AsSpan(14, 2));

        await SendCommandAsync(CommandRasterSplit, payload, cancellationToken).ConfigureAwait(false);
        return await WaitForAckAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool?> SendAudioCommandAsync(
        char subcmd,
        ReadOnlyMemory<byte> args,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var payload = new byte[1 + args.Length * 2];
        payload[0] = (byte)subcmd;

        for (var i = 0; i < args.Length; i++)
        {
            EncodeHexByte(args.Span[i], payload.AsSpan(1 + i * 2, 2));
        }

        await SendCommandAsync(CommandAudio, payload, cancellationToken).ConfigureAwait(false);
        return await WaitForAckAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendAudioStreamCommandAsync(
        char subcmd,
        ReadOnlyMemory<byte> args,
        CancellationToken cancellationToken = default)
    {
        var payload = new byte[1 + args.Length * 2];
        payload[0] = (byte)subcmd;

        for (var i = 0; i < args.Length; i++)
        {
            EncodeHexByte(args.Span[i], payload.AsSpan(1 + i * 2, 2));
        }

        await SendCommandAsync(CommandAudio, payload, cancellationToken).ConfigureAwait(false);
    }

    // --- SoundBridge Core Commands (Acknowledgeable) ---
    
    public Task<bool?> SoundBridgeResetAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('R', ReadOnlyMemory<byte>.Empty, timeout, cancellationToken);

    /// <summary>
    /// Relocates the SFX bytecode script bank to a new page-aligned base
    /// (command <c>AB</c>). <paramref name="basePage"/> is the high byte of
    /// the base address; the bank is always page-aligned (low byte $00) and
    /// each of the 16 slots is 64 bytes. Upload SFX scripts to
    /// <c>(basePage &lt;&lt; 8) + sfxId * 64</c> before calling
    /// <see cref="SoundBridgePlaySfxAsync(byte,byte,byte,TimeSpan,CancellationToken)"/>.
    /// Defaults to <c>$C0</c> ($C000) on the client until changed.
    /// </summary>
    public Task<bool?> SoundBridgeSetSfxBaseAsync(byte basePage, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('B', new[] { basePage }, timeout, cancellationToken);

    public Task<bool?> SoundBridgeSetVolumeAsync(byte volume, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('V', new[] { (byte)(volume & 0x0F) }, timeout, cancellationToken);

    public Task<bool?> SoundBridgeSetModeAsync(byte mode, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('M', new[] { mode }, timeout, cancellationToken);

    public Task<bool?> SoundBridgeDefineInstrumentAsync(byte id, ushort pulseWidth, byte attackDecay, byte sustainRelease, byte control, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('I', new[] { id, (byte)(pulseWidth & 0xFF), (byte)(pulseWidth >> 8), attackDecay, sustainRelease, control }, timeout, cancellationToken);

    public Task<bool?> SoundBridgePlaySfxAsync(byte sfxId, byte priority, byte flags, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('S', new[] { sfxId, priority, flags }, timeout, cancellationToken);

    public Task<bool?> SoundBridgeStopSfxAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('X', ReadOnlyMemory<byte>.Empty, timeout, cancellationToken);

    public Task<bool?> SoundBridgeStopAllAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('Z', ReadOnlyMemory<byte>.Empty, timeout, cancellationToken);

    // --- SoundBridge Real-Time Stream Commands (no-ACK) ---

    public Task SoundBridgeNoteOnAsync(byte voice, ushort sidFrequency, byte instrumentId, CancellationToken cancellationToken = default) =>
        SendAudioStreamCommandAsync('N', new[] { voice, (byte)(sidFrequency & 0xFF), (byte)(sidFrequency >> 8), instrumentId }, cancellationToken);

    public Task SoundBridgeNoteOffAsync(byte voice, CancellationToken cancellationToken = default) =>
        SendAudioStreamCommandAsync('O', new[] { voice }, cancellationToken);

    public Task SoundBridgeFullVoiceSetupAsync(byte voice, ushort sidFrequency, ushort pulseWidth, byte attackDecay, byte sustainRelease, byte control, CancellationToken cancellationToken = default) =>
        SendAudioStreamCommandAsync('F', new[] { voice, (byte)(sidFrequency & 0xFF), (byte)(sidFrequency >> 8), (byte)(pulseWidth & 0xFF), (byte)(pulseWidth >> 8), attackDecay, sustainRelease, control }, cancellationToken);

    public Task SoundBridgeSetFrequencyAsync(byte voice, ushort sidFrequency, CancellationToken cancellationToken = default) =>
        SendAudioStreamCommandAsync('Q', new[] { voice, (byte)(sidFrequency & 0xFF), (byte)(sidFrequency >> 8) }, cancellationToken);

    public Task SoundBridgeSetAdsrAsync(byte voice, byte attackDecay, byte sustainRelease, CancellationToken cancellationToken = default) =>
        SendAudioStreamCommandAsync('D', new[] { voice, attackDecay, sustainRelease }, cancellationToken);

    public Task SoundBridgeSetControlAsync(byte voice, byte control, CancellationToken cancellationToken = default) =>
        SendAudioStreamCommandAsync('W', new[] { voice, control }, cancellationToken);

    public Task SoundBridgeSetPulseWidthAsync(byte voice, ushort pulseWidth, CancellationToken cancellationToken = default) =>
        SendAudioStreamCommandAsync('P', new[] { voice, (byte)(pulseWidth & 0xFF), (byte)(pulseWidth >> 8) }, cancellationToken);

    public Task SoundBridgeSetEffectAsync(byte voice, byte effectType, byte speed, byte depth, CancellationToken cancellationToken = default) =>
        SendAudioStreamCommandAsync('E', new[] { voice, effectType, speed, depth }, cancellationToken);

    /// <summary>
    /// Enables a classic SID "chord" arpeggio on a voice: the voice rapidly
    /// cycles root -> third -> fifth so the ear hears a chord from one voice.
    /// </summary>
    /// <param name="voice">Target voice (0..2).</param>
    /// <param name="holdFrames">Frames each tone is held (0 = change every frame = fast buzz).</param>
    /// <param name="minor">True for a minor chord (0,+3,+7); false for major (0,+4,+7).</param>
    public Task SoundBridgeSetArpeggioAsync(byte voice, byte holdFrames, bool minor, CancellationToken cancellationToken = default) =>
        SoundBridgeSetEffectAsync(voice, effectType: 4, speed: holdFrames, depth: (byte)(minor ? 1 : 0), cancellationToken);

    public Task<bool?> StopAudioAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('0', ReadOnlyMemory<byte>.Empty, timeout, cancellationToken);

    public Task<bool?> StartAudioAsync(byte subtune, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('1', new[] { subtune }, timeout, cancellationToken);

    public Task<bool?> PauseAudioAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('2', ReadOnlyMemory<byte>.Empty, timeout, cancellationToken);

    public Task<bool?> ResumeAudioAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('3', ReadOnlyMemory<byte>.Empty, timeout, cancellationToken);

    public Task<bool?> BindAudioModuleAsync(ushort address, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('5', new[] { (byte)(address & 0xFF), (byte)(address >> 8) }, timeout, cancellationToken);

    public Task<bool?> SetAudioVolumeAsync(byte volume, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('6', new[] { (byte)(volume & 0x0F) }, timeout, cancellationToken);

    public Task<bool?> SetAudioTempoAsync(byte tempo, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('4', new[] { tempo }, timeout, cancellationToken);

    public async Task<byte?> QueryAudioStateAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var payload = new byte[1];
        payload[0] = (byte)'7';
        await SendCommandAsync(CommandAudio, payload, cancellationToken).ConfigureAwait(false);

        var buffer = new byte[1];
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            var read = await _connection.ReadAsync(buffer, timeoutSource.Token).ConfigureAwait(false);
            return read > 0 ? buffer[0] : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public Task StartTelemetryAsync(
        byte divider,
        byte channels,
        CancellationToken cancellationToken = default)
    {
        var payload = new byte[5];
        payload[0] = (byte)'1';
        EncodeHexByte(divider, payload.AsSpan(1, 2));
        EncodeHexByte(channels, payload.AsSpan(3, 2));

        return SendCommandAsync(CommandTelemetry, payload, cancellationToken);
    }

    /// <summary>
    /// Strongly-typed overload of <see cref="StartTelemetryAsync(byte,byte,CancellationToken)"/>.
    /// </summary>
    public Task StartTelemetryAsync(
        byte divider,
        TelemetryChannels channels,
        CancellationToken cancellationToken = default) =>
        StartTelemetryAsync(divider, (byte)channels, cancellationToken);

    public Task StopTelemetryAsync(CancellationToken cancellationToken = default)
    {
        var payload = new[] { (byte)'0' };
        return SendCommandAsync(CommandTelemetry, payload, cancellationToken);
    }

    public Task RequestOneShotTelemetryAsync(CancellationToken cancellationToken = default)
    {
        var payload = new[] { (byte)'2' };
        return SendCommandAsync(CommandTelemetry, payload, cancellationToken);
    }

    public Task SetSpriteConfigAsync(
        IReadOnlyList<Rift64SpriteConfig> sprites,
        CancellationToken cancellationToken = default)
    {
        var payload = new byte[sprites.Count * 17];
        for (int i = 0; i < sprites.Count; i++)
        {
            var spr = sprites[i];
            int offset = i * 17;
            payload[offset] = CommandSpriteSet;
            EncodeHexByte(spr.Id, payload.AsSpan(offset + 1, 2));
            EncodeHexByte((byte)((spr.X >> 8) & 0x01), payload.AsSpan(offset + 3, 2));
            EncodeHexByte((byte)(spr.X & 0xFF), payload.AsSpan(offset + 5, 2));
            EncodeHexByte(spr.Y, payload.AsSpan(offset + 7, 2));
            EncodeHexByte((byte)(spr.Color & 0x0F), payload.AsSpan(offset + 9, 2));
            EncodeHexByte(spr.Pointer, payload.AsSpan(offset + 11, 2));
            EncodeHexByte(spr.Bank.CiaPortABits(), payload.AsSpan(offset + 13, 2));
            EncodeHexByte((byte)(spr.Enabled ? 1 : 0), payload.AsSpan(offset + 15, 2));
        }
        return _connection.WriteAsync(payload, cancellationToken).AsTask();
    }

    public ValueTask<int> ReadRawAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return _connection.ReadAsync(buffer, cancellationToken);
    }

    /// <summary>
    /// Drains any bytes buffered during ACK waits (e.g. telemetry data received between commands).
    /// </summary>
    public byte[] DrainPendingBytes()
    {
        if (_pendingBytes.Count == 0)
            return Array.Empty<byte>();

        var result = _pendingBytes.ToArray();
        _pendingBytes.Clear();
        return result;
    }

    public bool HasPendingBytes => _pendingBytes.Count > 0;

    public bool IsDataAvailable => _connection.IsDataAvailable;

    public async Task<bool?> DrawMetatileAsync(
        byte mode,
        ushort mapAddr,
        byte mapW,
        byte mapH,
        byte metaHi,
        ushort targetAddr,
        byte stride,
        byte winW,
        byte winH,
        byte x,
        byte y,
        byte offX,
        byte offY,
        byte fillChar = 32,
        byte colorMode = 0,
        ushort colorTgtAddr = 0xD800,
        ushort colorSrcAddr = 0,
        byte colorFill = 14,
        TimeSpan timeout = default,
        CancellationToken cancellationToken = default)
    {
        if (timeout == default)
        {
            timeout = TimeSpan.FromSeconds(2);
        }

        var payload = new byte[44];

        EncodeHexByte(mode, payload.AsSpan(0, 2));
        EncodeHexByte((byte)(mapAddr & 0xFF), payload.AsSpan(2, 2));
        EncodeHexByte((byte)(mapAddr >> 8), payload.AsSpan(4, 2));
        EncodeHexByte(mapW, payload.AsSpan(6, 2));
        EncodeHexByte(mapH, payload.AsSpan(8, 2));
        EncodeHexByte(metaHi, payload.AsSpan(10, 2));
        EncodeHexByte((byte)(targetAddr & 0xFF), payload.AsSpan(12, 2));
        EncodeHexByte((byte)(targetAddr >> 8), payload.AsSpan(14, 2));
        EncodeHexByte(stride, payload.AsSpan(16, 2));
        EncodeHexByte(winW, payload.AsSpan(18, 2));
        EncodeHexByte(winH, payload.AsSpan(20, 2));
        EncodeHexByte(x, payload.AsSpan(22, 2));
        EncodeHexByte(y, payload.AsSpan(24, 2));
        EncodeHexByte(offX, payload.AsSpan(26, 2));
        EncodeHexByte(offY, payload.AsSpan(28, 2));
        EncodeHexByte(fillChar, payload.AsSpan(30, 2));
        EncodeHexByte(colorMode, payload.AsSpan(32, 2));
        EncodeHexByte((byte)(colorTgtAddr & 0xFF), payload.AsSpan(34, 2));
        EncodeHexByte((byte)(colorTgtAddr >> 8), payload.AsSpan(36, 2));
        EncodeHexByte((byte)(colorSrcAddr & 0xFF), payload.AsSpan(38, 2));
        EncodeHexByte((byte)(colorSrcAddr >> 8), payload.AsSpan(40, 2));
        EncodeHexByte(colorFill, payload.AsSpan(42, 2));

        await SendCommandAsync(CommandDrawMetatile, payload, cancellationToken).ConfigureAwait(false);
        return await WaitForAckAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Strongly-typed convenience overload of
    /// <see cref="DrawMetatileAsync(byte,ushort,byte,byte,byte,ushort,byte,byte,byte,byte,byte,byte,byte,byte,byte,ushort,ushort,byte,TimeSpan,CancellationToken)"/>
    /// using <see cref="MetatileColorMode"/>. Pass the colour mode by enum
    /// value via the <c>color</c> parameter.
    /// </summary>
    /// <remarks>
    /// <see cref="MetatileColorMode.MapPerCell"/> requires <paramref name="colorSrcAddr"/>
    /// to be page-aligned (low byte must be 0). For mode 2 the bank at that
    /// address is 4 page-aligned slot pages (1 KB total) authored with
    /// <see cref="BuildMode2PerCellColorBank"/>. On mode 3 the renderer falls
    /// back to <see cref="MetatileColorMode.Map"/> semantics (one colour per
    /// tile id).
    /// </remarks>
    public Task<bool?> DrawMetatileAsync(
        byte mode,
        ushort mapAddr,
        byte mapW,
        byte mapH,
        byte metaHi,
        ushort targetAddr,
        byte stride,
        byte winW,
        byte winH,
        byte x,
        byte y,
        byte offX,
        byte offY,
        MetatileColorMode color,
        ushort colorSrcAddr = 0,
        byte fillChar = 32,
        ushort colorTgtAddr = 0xD800,
        byte colorFill = 14,
        TimeSpan timeout = default,
        CancellationToken cancellationToken = default) =>
        DrawMetatileAsync(
            mode, mapAddr, mapW, mapH, metaHi,
            targetAddr, stride, winW, winH, x, y, offX, offY,
            fillChar, (byte)color, colorTgtAddr, colorSrcAddr, colorFill,
            timeout, cancellationToken);

    /// <summary>
    /// Builds the 1 KB colour bank for a mode-2 metatile <c>D</c> command with
    /// <see cref="MetatileColorMode.MapPerCell"/>. Layout matches the renderer:
    /// 4 page-aligned slot pages indexed by tile id, slot order = TL, TR, BL, BR.
    /// </summary>
    /// <param name="perTilePalette">
    /// Up to 256 entries, one per tile id. Each entry is exactly 4 colour
    /// nibbles in slot order (TL, TR, BL, BR). Tile ids beyond the supplied
    /// length default to <c>defaultColor</c> for every cell.
    /// </param>
    /// <param name="defaultColor">Colour-RAM nibble used for unsupplied tile ids.</param>
    public static byte[] BuildMode2PerCellColorBank(
        IReadOnlyList<(byte TopLeft, byte TopRight, byte BottomLeft, byte BottomRight)> perTilePalette,
        byte defaultColor = 14)
    {
        var bank = new byte[1024];
        if (defaultColor != 0)
        {
            for (int i = 0; i < bank.Length; i++) bank[i] = defaultColor;
        }
        int n = Math.Min(perTilePalette.Count, 256);
        for (int id = 0; id < n; id++)
        {
            var (tl, tr, bl, br) = perTilePalette[id];
            bank[0 * 256 + id] = tl;
            bank[1 * 256 + id] = tr;
            bank[2 * 256 + id] = bl;
            bank[3 * 256 + id] = br;
        }
        return bank;
    }

    public async Task<char?> ReadKeyAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[1];
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            var read = await _connection.ReadAsync(buffer, timeoutSource.Token).ConfigureAwait(false);
            if (read <= 0)
            {
                return null;
            }

            return _textConverter.DecodeByte(buffer[0]);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public async Task<string> ReadLineAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var collected = new List<byte>();
        var buffer = new byte[1];

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        while (!timeoutSource.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await _connection.ReadAsync(buffer, timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (read <= 0)
            {
                break;
            }

            var value = buffer[0];
            if (value == 13 || value == 10)
            {
                if (collected.Count > 0)
                {
                    break;
                }

                continue;
            }

            collected.Add(value);
        }

        return _textConverter.Decode(collected.ToArray());
    }

    private async Task SendCommandAsync(byte command, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var data = new byte[payload.Length + 1];
        data[0] = command;
        payload.CopyTo(data.AsMemory(1));
        await _connection.WriteAsync(data, cancellationToken).ConfigureAwait(false);
    }

    private static string ExtractClientVersion(string capabilities)
    {
        var versionMatch = VersionRegex.Match(capabilities);
        if (!versionMatch.Success)
        {
            return "unknown";
        }

        return versionMatch.Groups["version"].Value;
    }

    private static bool IsCapabilitiesResponse(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains("RIFT64", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool?> WaitForAckAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        while (!timeoutSource.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await _connection.ReadAsync(buffer, timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (read <= 0)
            {
                continue;
            }

            var value = (byte)(buffer[0] & 0x7F);
            if (value == AckByte)
            {
                return true;
            }

            if (value == NakByte)
            {
                return false;
            }

            // Buffer unsolicited bytes (e.g. telemetry) instead of discarding them
            _pendingBytes.Enqueue(buffer[0]);
            UnsolicitedByteReceived?.Invoke(buffer[0]);
        }

        return null;
    }

    private static byte[] BuildMemoryStorePayload(ushort address, ReadOnlySpan<byte> data, bool includeChecksum)
    {
        var payloadSize = includeChecksum ? 8 + data.Length : 6 + data.Length;
        var payload = new byte[payloadSize];
        var destHi = (byte)(address >> 8);
        var destLo = (byte)(address & 0xFF);
        var encodedLength = data.Length == 256 ? (byte)0 : (byte)data.Length;

        EncodeHexByte(destHi, payload.AsSpan(0, 2));
        EncodeHexByte(destLo, payload.AsSpan(2, 2));
        EncodeHexByte(encodedLength, payload.AsSpan(4, 2));
        data.CopyTo(payload.AsSpan(6));

        if (includeChecksum)
        {
            var checksum = ProtocolCodec.ComputeRollingChecksum(data);
            EncodeHexByte(checksum, payload.AsSpan(6 + data.Length, 2));
        }

        return payload;
    }

    private static byte[] BuildCheckedWindowPayload(byte width, byte height, int required, ReadOnlySpan<byte> content)
    {
        var payload = new byte[6 + required];

        EncodeHexByte(width, payload.AsSpan(0, 2));
        EncodeHexByte(height, payload.AsSpan(2, 2));

        var copy = Math.Min(content.Length, required);
        if (copy > 0)
        {
            content[..copy].CopyTo(payload.AsSpan(4, copy));
        }

        for (var i = copy; i < required; i++)
        {
            payload[4 + i] = (byte)' ';
        }

        var checksum = ProtocolCodec.ComputeRollingChecksum(payload.AsSpan(4, required));
        EncodeHexByte(checksum, payload.AsSpan(4 + required, 2));

        return payload;
    }

    private static byte[] BuildWindowPayload(byte width, byte height, ReadOnlySpan<byte> content)
    {
        byte effectiveWidth = ProtocolCodec.ClampWidth(width);
        byte effectiveHeight = ProtocolCodec.ClampHeight(height);
        var required = effectiveWidth * effectiveHeight;

        var payload = new byte[4 + required];
        EncodeHexByte(width, payload.AsSpan(0, 2));
        EncodeHexByte(height, payload.AsSpan(2, 2));

        var copy = Math.Min(content.Length, required);
        if (copy > 0)
        {
            content[..copy].CopyTo(payload.AsSpan(4, copy));
        }

        for (var i = copy; i < required; i++)
        {
            payload[4 + i] = (byte)' ';
        }

        return payload;
    }

    private static void ValidateBufferIndex(byte bufferIndex)
    {
        if (bufferIndex > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferIndex), "Buffer index must be 0 or 1.");
        }
    }

    private static void ValidateStoreLength(int length, string paramName)
    {
        if (length <= 0 || length > 256)
        {
            throw new ArgumentOutOfRangeException(paramName, "Payload length must be 1..256 bytes.");
        }
    }

    private static byte[] EncodeScreenCodes(string text)
    {
        if (text == null)
        {
            return Array.Empty<byte>();
        }

        var result = new byte[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            switch (ch)
            {
                case '▒':
                    result[i] = 102; // Checkerboard graphics code on C64
                    break;
                case '█':
                    result[i] = 160; // Solid block code on C64
                    break;
                case '●':
                    result[i] = 81;  // Solid circle code on C64
                    break;
                case '○':
                    result[i] = 87;  // Open circle code on C64
                    break;
                case '♥':
                    result[i] = 83;  // Heart code on C64
                    break;
                case '♦':
                    result[i] = 90;  // Diamond code on C64
                    break;
                case '♣':
                    result[i] = 88;  // Club code on C64
                    break;
                case '♠':
                    result[i] = 65;  // Spade code on C64
                    break;
                case '▪':
                    result[i] = 119; // Small square/bullet code on C64
                    break;
                default:
                    var up = char.ToUpperInvariant(ch);
                    var val = (byte)(up & 0x7F);
                    if (val >= 65 && val <= 90)
                    {
                        val -= 64;
                    }
                    result[i] = val;
                    break;
            }
        }

        return result;
    }

    private static byte BufferIndexByte(byte bufferIndex)
    {
        return bufferIndex == 1 ? (byte)'1' : (byte)'0';
    }

    private static void EncodeHexByte(byte value, Span<byte> destination)
    {
        destination[0] = EncodeHexNibble((byte)(value >> 4));
        destination[1] = EncodeHexNibble((byte)(value & 0x0F));
    }

    private static byte EncodeHexNibble(byte value)
    {
        value &= 0x0F;
        return value < 10
            ? (byte)('0' + value)
            : (byte)('A' + (value - 10));
    }
}