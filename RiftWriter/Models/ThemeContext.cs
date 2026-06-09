namespace RiftWriter.Models;

/// <summary>
/// Mutable active-theme color holder. Owned per-session (no globals).
/// </summary>
internal sealed class ThemeContext
{
    public byte Bg { get; set; }
    public byte TitleBg { get; set; }
    public byte TitleFg { get; set; }
    public byte MenuFg { get; set; }
    public byte EditorFg { get; set; }
    public byte StatusBg { get; set; }
    public byte StatusFg { get; set; }
    public byte RulerFg { get; set; }
    public byte PopupBg { get; set; }
    public byte PopupFg { get; set; }
    public byte PopupBorder { get; set; }
    public byte Highlight { get; set; }

    public int ThemeIndex { get; set; }

    public void ApplyTheme(int index)
    {
        var theme = WriterTheme.BuiltInThemes[index];
        ThemeIndex = index;
        Bg = theme.Bg;
        TitleBg = theme.TitleBg;
        TitleFg = theme.TitleFg;
        MenuFg = theme.MenuFg;
        EditorFg = theme.EditorFg;
        StatusBg = theme.StatusBg;
        StatusFg = theme.StatusFg;
        RulerFg = theme.RulerFg;
        PopupBg = theme.PopupBg;
        PopupFg = theme.PopupFg;
        PopupBorder = theme.PopupBorder;
        Highlight = theme.Highlight;
    }

    public static ThemeContext CreateDefault()
    {
        var ctx = new ThemeContext();
        ctx.ApplyTheme(0);
        return ctx;
    }
}
