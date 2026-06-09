namespace RiftServe64.Sdk.Protocol;

public readonly record struct Rift64BorderGlyphs(
    byte TopLeft,
    byte Top,
    byte TopRight,
    byte Left,
    byte Right,
    byte BottomLeft,
    byte Bottom,
    byte BottomRight)
{
    /// <summary>Classic rounded-corner box (PETSCII default).</summary>
    public static Rift64BorderGlyphs Default { get; } = new(
        TopLeft: 0x70,
        Top: 0x40,
        TopRight: 0x6E,
        Left: 0x42,
        Right: 0x42,
        BottomLeft: 0x6D,
        Bottom: 0x40,
        BottomRight: 0x7D);

    /// <summary>Same as <see cref="Default"/>; explicit alias.</summary>
    public static Rift64BorderGlyphs Classic => Default;

    /// <summary>Solid block on every edge and corner.</summary>
    public static Rift64BorderGlyphs Solid { get; } = new(
        TopLeft: 0xA0, Top: 0xA0, TopRight: 0xA0,
        Left: 0xA0, Right: 0xA0,
        BottomLeft: 0xA0, Bottom: 0xA0, BottomRight: 0xA0);

    /// <summary>ASCII-style frame using <c>+</c> corners and <c>- |</c> edges.</summary>
    public static Rift64BorderGlyphs Ascii { get; } = new(
        TopLeft: (byte)'+', Top: (byte)'-', TopRight: (byte)'+',
        Left: (byte)'|', Right: (byte)'|',
        BottomLeft: (byte)'+', Bottom: (byte)'-', BottomRight: (byte)'+');

    /// <summary>All edges drawn with the same character.</summary>
    public static Rift64BorderGlyphs Uniform(byte glyph) =>
        new(glyph, glyph, glyph, glyph, glyph, glyph, glyph, glyph);

    /// <summary>
    /// Build glyphs from an 8-character string in the order
    /// TL, T, TR, L, R, BL, B, BR.
    /// </summary>
    public static Rift64BorderGlyphs From(string eightChars)
    {
        ArgumentNullException.ThrowIfNull(eightChars);
        if (eightChars.Length != 8)
            throw new ArgumentException("Must be exactly 8 characters.", nameof(eightChars));
        return new Rift64BorderGlyphs(
            (byte)eightChars[0], (byte)eightChars[1], (byte)eightChars[2],
            (byte)eightChars[3], (byte)eightChars[4],
            (byte)eightChars[5], (byte)eightChars[6], (byte)eightChars[7]);
    }
}