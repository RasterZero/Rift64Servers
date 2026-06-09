using RiftServe64.Sdk.Protocol;
using RiftWriter.Models;

namespace RiftWriter.Rendering;

/// <summary>
/// Draws title bar, menu bar, ruler, and status bar. Implements the editor layout chrome.
/// </summary>
internal sealed class ChromeRenderer
{
    private readonly Rift64ProtocolClient _client;
    private readonly ThemeContext _theme;

    public ChromeRenderer(Rift64ProtocolClient client, ThemeContext theme)
    {
        _client = client;
        _theme = theme;
    }

    public async Task DrawTitleBarAsync(string timerStr = "", CancellationToken ct = default)
    {
        var baseTitle = " Rift Writer v1.0 ".PadLeft((WriterConstants.ScreenWidth + 18) / 2).PadRight(WriterConstants.ScreenWidth);
        string title;
        if (!string.IsNullOrEmpty(timerStr))
        {
            var chars = baseTitle.ToCharArray();
            var timerLen = timerStr.Length;
            for (var i = 0; i < timerLen && (WriterConstants.ScreenWidth - timerLen + i) < WriterConstants.ScreenWidth; i++)
                chars[WriterConstants.ScreenWidth - timerLen + i] = timerStr[i];
            title = new string(chars);
        }
        else
        {
            title = baseTitle;
        }
        await DrawUniformRowAsync(0, 0, WriterConstants.ScreenWidth,
            LowercaseScreenCodeConverter.EncodeCached(title), _theme.TitleFg, ct);
    }

    public async Task DrawMenuBarAsync(CancellationToken ct = default)
    {
        const string menu = " F1:File  F3:Edit  F5:View  F7:Help   ";
        await DrawUniformRowAsync(0, 1, WriterConstants.ScreenWidth,
            LowercaseScreenCodeConverter.EncodeCached(menu), _theme.MenuFg, ct);
    }

    public async Task DrawRulerAsync(int viewportX, CancellationToken ct = default)
    {
        var leftCol = viewportX + 1;
        var rightCol = viewportX + WriterConstants.ViewportCols;
        var rangeText = $"col {leftCol}-{rightCol}";
        var ruler = "----" + $" {rangeText} " + new string('-', WriterConstants.ScreenWidth - rangeText.Length - 6);
        if (ruler.Length > WriterConstants.ScreenWidth)
            ruler = ruler[..WriterConstants.ScreenWidth];
        await DrawUniformRowAsync(0, 23, WriterConstants.ScreenWidth,
            LowercaseScreenCodeConverter.Encode(ruler), _theme.RulerFg, ct);
    }

    public async Task DrawStatusBarAsync(WriterDocument doc, int cursorRow, int cursorCol, bool insertMode, CancellationToken ct = default)
    {
        var modFlag = doc.Modified ? "*" : " ";
        var modeStr = insertMode ? "ins" : "ovr";
        var fname = doc.Filename.Length > 16 ? doc.Filename[..16].ToLowerInvariant() : doc.Filename.ToLowerInvariant();
        var status = $" {modFlag}{fname,-16} ln:{cursorRow + 1,-4} col:{cursorCol + 1,-3} {modeStr}";
        if (status.Length > WriterConstants.ScreenWidth)
            status = status[..WriterConstants.ScreenWidth];
        status = status.PadRight(WriterConstants.ScreenWidth);
        await DrawUniformRowAsync(0, 24, WriterConstants.ScreenWidth,
            LowercaseScreenCodeConverter.Encode(status), _theme.StatusFg, ct);
    }

    public async Task DrawEditorChromeAsync(WriterDocument doc, int viewportX, int cursorRow, int cursorCol, bool insertMode, string timerStr = "", CancellationToken ct = default)
    {
        await DrawTitleBarAsync(timerStr, ct);
        await DrawMenuBarAsync(ct);
        await DrawRulerAsync(viewportX, ct);
        await DrawStatusBarAsync(doc, cursorRow, cursorCol, insertMode, ct);
    }

    /// <summary>
    /// Update just the timer portion of the title bar by writing directly to screen + color RAM.
    /// Does NOT move the hardware cursor.
    /// </summary>
    public async Task UpdateTimerInPlaceAsync(string timerStr, CancellationToken ct = default)
    {
        const int timerMaxLen = 7; // "[MM:SS]"
        var display = timerStr.Length > timerMaxLen ? timerStr[..timerMaxLen] : timerStr.PadLeft(timerMaxLen);
        var screenCodes = LowercaseScreenCodeConverter.Encode(display);

        // Screen RAM row 0, right-aligned: address = 0x0400 + (40 - timerMaxLen)
        ushort screenAddr = (ushort)(0x0400 + WriterConstants.ScreenWidth - timerMaxLen);
        await _client.StoreMemoryAsync(screenAddr, screenCodes, ct);

        // Color RAM row 0, same offset: address = 0xD800 + (40 - timerMaxLen)
        ushort colorAddr = (ushort)(0xD800 + WriterConstants.ScreenWidth - timerMaxLen);
        var colors = new byte[timerMaxLen];
        Array.Fill(colors, _theme.TitleFg);
        await _client.StoreMemoryAsync(colorAddr, colors, ct);
    }

    private async Task DrawUniformRowAsync(byte x, byte y, int width, byte[] screenCodes, byte color, CancellationToken ct)
    {
        await _client.SetCursorAsync(x, y, ct);
        await _client.DrawColoredWindowRawAsync((Rift64Color)color, (byte)width, 1, screenCodes, ct);
    }
}
