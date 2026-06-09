namespace RiftWriter.Rendering;

/// <summary>
/// Converts ASCII/Unicode text to native C64 screen codes for the
/// lowercase/uppercase charset ($D018=$17).
/// </summary>
internal static class LowercaseScreenCodeConverter
{
    private static readonly Dictionary<string, byte[]> Cache = new(256);

    public static byte[] Encode(string text)
    {
        var result = new byte[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var value = (int)text[i];
            int code;

            if (value >= 32 && value <= 63)
                code = value;
            else if (value == 64)
                code = 0;
            else if (value >= 65 && value <= 90)
                code = value; // A-Z → screen 65-90
            else if (value >= 91 && value <= 95)
                code = value - 64;
            else if (value >= 97 && value <= 122)
                code = value - 96; // a-z → screen 1-26
            else if (value >= 160 && value <= 191)
                code = value - 96;
            else if (value >= 192 && value <= 223)
                code = value - 128;
            else if (value >= 224 && value <= 254)
                code = value - 144;
            else if (value == 255)
                code = 94;
            else
                code = 32; // fallback to space

            result[i] = (byte)(code & 0x7F);
        }
        return result;
    }

    /// <summary>
    /// Cached version for static UI strings (chrome, hints).
    /// </summary>
    public static byte[] EncodeCached(string text)
    {
        if (Cache.TryGetValue(text, out var cached))
            return cached;

        var encoded = Encode(text);
        if (Cache.Count < 256)
            Cache[text] = encoded;
        return encoded;
    }
}
