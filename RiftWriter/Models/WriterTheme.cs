namespace RiftWriter.Models;

/// <summary>
/// Immutable theme definition.
/// </summary>
internal sealed record WriterTheme(
    string Name,
    byte Bg,
    byte TitleBg,
    byte TitleFg,
    byte MenuFg,
    byte EditorFg,
    byte StatusBg,
    byte StatusFg,
    byte RulerFg,
    byte PopupBg,
    byte PopupFg,
    byte PopupBorder,
    byte Highlight)
{
    public static readonly WriterTheme[] BuiltInThemes =
    [
        new("Classic Matrix",    0, 14, 1, 15, 13, 14, 1, 12, 0, 1, 12, 3),
        new("Classic C64",       6, 14, 1, 14, 14, 14, 1, 15, 6, 1, 14, 3),
        new("Monochrome Paper",  1, 12, 0, 11,  0, 12, 0, 11, 1, 0, 12, 6),
        new("Retro Amber",       0,  9, 8, 15,  8,  9, 8, 11, 0, 8, 12, 7),
        new("Sunset Violet",     4, 10, 1, 15, 10, 10, 1, 12, 4, 1, 10, 7),
    ];
}
