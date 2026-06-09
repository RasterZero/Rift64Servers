namespace RiftServe64.Sdk.Protocol;

public interface IRift64TextConverter
{
    byte[] Encode(string text);
    char DecodeByte(byte value);
    string Decode(ReadOnlySpan<byte> values);
}