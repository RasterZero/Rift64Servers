namespace RiftServe64.Sdk.Protocol;

[Flags]
public enum JoyDirection : byte
{
    None  = 0x00,
    Up    = 0x01,
    Down  = 0x02,
    Left  = 0x04,
    Right = 0x08,
    Fire  = 0x10
}

[Flags]
public enum TelemetryChannels : byte
{
    None  = 0x00,
    /// <summary>Joystick port 2 (CIA1 $DC00).</summary>
    Joy1  = 0x01,
    /// <summary>Joystick port 1 (CIA1 $DC01).</summary>
    Joy2  = 0x02,
    /// <summary>Sprite-to-sprite collision latch ($D01E).</summary>
    SpriteToSprite = 0x04,
    /// <summary>Sprite-to-background collision latch ($D01F).</summary>
    SpriteToBackground = 0x08,
    All   = Joy1 | Joy2 | SpriteToSprite | SpriteToBackground
}

/// <summary>
/// Colour-RAM behaviour for the D (DrawMetatile) command.
/// </summary>
public enum MetatileColorMode : byte
{
    /// <summary>Skip colour RAM writes entirely.</summary>
    None    = 0,
    /// <summary>Every cell in the window gets <c>colorFill</c>.</summary>
    Fill    = 1,
    /// <summary>
    /// Mode 1: parallel per-cell colour map at <c>colorSrcAddr</c>.
    /// Modes 2/3: 256-byte per-tile-id colour table at <c>colorSrcAddr</c>
    /// (one colour per metatile id, applied to every child cell of that tile).
    /// </summary>
    Map     = 2,
    /// <summary>
    /// Mode 1: same as <see cref="Map"/>.
    /// Modes 2/3: parallel slot pages, one full colour byte per child cell of
    /// each metatile id. Bank size = 1 KB (mode 2) or 2.25 KB (mode 3) and
    /// MUST be page-aligned. <c>colorSrcAddr</c> low byte is ignored;
    /// only the page (high byte) is used.
    /// </summary>
    MapPerCell = 3,
}

public sealed record Rift64ClientIdentity(
    string CapabilitiesRaw,
    string ClientVersion,
    bool IsCompatible);

public sealed record Rift64TelemetryFrame(
    byte Seq,
    byte Joy1,
    byte Joy2,
    byte SprSpr,
    byte SprBg)
{
    public const byte JoyUp = 0x01;
    public const byte JoyDown = 0x02;
    public const byte JoyLeft = 0x04;
    public const byte JoyRight = 0x08;
    public const byte JoyFire = 0x10;

    /// <summary>Typed view of <see cref="Joy1"/> (joystick port 2).</summary>
    public JoyDirection Joy1Directions => (JoyDirection)Joy1;
    /// <summary>Typed view of <see cref="Joy2"/> (joystick port 1).</summary>
    public JoyDirection Joy2Directions => (JoyDirection)Joy2;

    public string GetJoy1Directions() => GetJoyDirections(Joy1);
    public string GetJoy2Directions() => GetJoyDirections(Joy2);

    private static string GetJoyDirections(byte val)
    {
        var parts = new List<string>();
        if ((val & JoyUp) != 0) parts.Add("UP");
        if ((val & JoyDown) != 0) parts.Add("DOWN");
        if ((val & JoyLeft) != 0) parts.Add("LEFT");
        if ((val & JoyRight) != 0) parts.Add("RIGHT");
        if ((val & JoyFire) != 0) parts.Add("FIRE");
        return parts.Count > 0 ? string.Join("+", parts) : "---";
    }
}

public sealed class Rift64TelemetryParser
{
    private const byte SyncMarker = 0x7E;
    private const byte TypeId = 0x55;
    private const int PacketLength = 8;

    private readonly List<byte> _buffer = new();
    private string _state = "seek"; // seek | header | payload

    public event Action<Rift64TelemetryFrame>? FrameReceived;

    public bool Feed(byte b)
    {
        if (_state == "seek")
        {
            if (b == SyncMarker)
            {
                _buffer.Clear();
                _buffer.Add(b);
                _state = "header";
                return true;
            }
            return false;
        }
        else if (_state == "header")
        {
            if (b == TypeId)
            {
                _buffer.Add(b);
                _state = "payload";
            }
            else
            {
                _state = "seek";
                if (b == SyncMarker)
                {
                    _buffer.Clear();
                    _buffer.Add(b);
                    _state = "header";
                }
            }
            return true;
        }
        else if (_state == "payload")
        {
            _buffer.Add(b);
            if (_buffer.Count == PacketLength)
            {
                TryDecode();
                _state = "seek";
            }
            return true;
        }
        return false;
    }

    private void TryDecode()
    {
        // Checksum calculation: sum of bytes 2..6, mask to 0xFF, compared to byte 7
        int sum = 0;
        for (var i = 2; i < 7; i++)
        {
            sum += _buffer[i];
        }

        var expectedChecksum = (byte)(sum & 0xFF);
        var actualChecksum = _buffer[7];

        if (expectedChecksum == actualChecksum)
        {
            var frame = new Rift64TelemetryFrame(
                Seq: _buffer[2],
                Joy1: _buffer[3],
                Joy2: _buffer[4],
                SprSpr: _buffer[5],
                SprBg: _buffer[6]
            );

            FrameReceived?.Invoke(frame);
        }
    }

    public void Reset()
    {
        _buffer.Clear();
        _state = "seek";
    }
}

/// <summary>
/// Single-sprite slot configuration used by <c>Rift64ProtocolClient.SetSpriteConfigAsync</c>.
/// </summary>
public sealed record Rift64SpriteConfig(
    byte Id,
    int X,
    byte Y,
    byte Color,
    byte Pointer,
    VicBank Bank,
    bool Enabled);