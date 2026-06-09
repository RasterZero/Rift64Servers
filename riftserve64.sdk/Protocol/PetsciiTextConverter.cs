using System.Text;

namespace RiftServe64.Sdk.Protocol;

public sealed class PetsciiTextConverter : IRift64TextConverter
{
    public static PetsciiTextConverter Default { get; } = new();

    public byte[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var output = new byte[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            output[i] = EncodeChar(text[i]);
        }

        return output;
    }

    public char DecodeByte(byte value)
    {
        if (value is >= (byte)'A' and <= (byte)'Z')
        {
            return (char)value;
        }

        if (value is >= 0xC1 and <= 0xDA)
        {
            return (char)(value - 0x80);
        }

        if (value is >= (byte)'0' and <= (byte)'9')
        {
            return (char)value;
        }

        if (value is >= 32 and <= 126)
        {
            return (char)value;
        }

        return value switch
        {
            13 => '\n',
            10 => '\n',
            133 => (char)133, // F1
            134 => (char)134, // F3
            135 => (char)135, // F5
            136 => (char)136, // F7
            137 => (char)137, // F2
            138 => (char)138, // F4
            139 => (char)139, // F6
            140 => (char)140, // F8
            _ => '?'
        };
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

    private static byte EncodeChar(char c)
    {
        if (c is '\r' or '\n')
        {
            return 13;
        }

        if (c == '\t')
        {
            return (byte)' ';
        }

        if (c is >= 'a' and <= 'z')
        {
            return (byte)(c - 32);
        }

        if (c is >= 'A' and <= 'Z')
        {
            return (byte)c;
        }

        if (c is >= '0' and <= '9')
        {
            return (byte)c;
        }

        if (c is >= ' ' and <= '~')
        {
            return (byte)c;
        }

        return (byte)'?';
    }
}