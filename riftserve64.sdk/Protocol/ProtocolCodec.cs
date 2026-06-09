namespace RiftServe64.Sdk.Protocol;

public static class ProtocolCodec
{
    public static byte ParseHexNibble(byte rawByte)
    {
        var value = (byte)(rawByte & 0x7F);

        if (value >= (byte)'0' && value <= (byte)'9')
        {
            return (byte)(value - (byte)'0');
        }

        if (value >= (byte)'A' && value <= (byte)'F')
        {
            return (byte)(value - 55);
        }

        return 0;
    }

    public static byte ParseHexByte(byte highNibbleRawByte, byte lowNibbleRawByte)
    {
        var highNibble = ParseHexNibble(highNibbleRawByte);
        var lowNibble = ParseHexNibble(lowNibbleRawByte);
        return (byte)((highNibble << 4) | lowNibble);
    }

    public static byte ComputeRollingChecksum(ReadOnlySpan<byte> payload, byte seed = 0)
    {
        var checksum = seed;
        foreach (var value in payload)
        {
            checksum = unchecked((byte)(checksum + value));
        }

        return checksum;
    }

    public static byte ClampWidth(byte value)
    {
        if (value == 0)
        {
            return 1;
        }

        return value > 40 ? (byte)40 : value;
    }

    public static byte ClampHeight(byte value)
    {
        if (value == 0)
        {
            return 1;
        }

        return value > 25 ? (byte)25 : value;
    }
}