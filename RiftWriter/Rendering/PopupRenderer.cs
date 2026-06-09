using RiftServe64.Sdk.Protocol;
using RiftWriter.Models;

namespace RiftWriter.Rendering;

/// <summary>
/// Draws PETSCII-bordered popups, menu popups, file browser, help screen.
/// Draws standard UI boundaries, menu listings, and alerts.
/// </summary>
internal sealed class PopupRenderer
{
    private readonly Rift64ProtocolClient _client;
    private readonly ThemeContext _theme;

    public PopupRenderer(Rift64ProtocolClient client, ThemeContext theme)
    {
        _client = client;
        _theme = theme;
    }

    public async Task DrawPopupBorderAsync(int x, int y, int w, int h, CancellationToken ct)
    {
        var totalW = w + 2;
        var top = new byte[totalW];
        top[0] = WriterConstants.BorderTL;
        for (var i = 1; i <= w; i++) top[i] = WriterConstants.BorderH;
        top[totalW - 1] = WriterConstants.BorderTR;

        var bot = new byte[totalW];
        bot[0] = WriterConstants.BorderBL;
        for (var i = 1; i <= w; i++) bot[i] = WriterConstants.BorderH;
        bot[totalW - 1] = WriterConstants.BorderBR;

        var mid = new byte[totalW];
        mid[0] = WriterConstants.BorderV;
        for (var i = 1; i <= w; i++) mid[i] = 0x20;
        mid[totalW - 1] = WriterConstants.BorderV;

        await _client.SetCursorAsync((byte)x, (byte)y, ct);
        await _client.DrawColoredWindowRawAsync((Rift64Color)_theme.PopupFg, (byte)totalW, 1, top, ct);
        for (var row = 0; row < h; row++)
        {
            await _client.SetCursorAsync((byte)x, (byte)(y + 1 + row), ct);
            await _client.DrawColoredWindowRawAsync((Rift64Color)_theme.PopupFg, (byte)totalW, 1, mid, ct);
        }
        await _client.SetCursorAsync((byte)x, (byte)(y + h + 1), ct);
        await _client.DrawColoredWindowRawAsync((Rift64Color)_theme.PopupFg, (byte)totalW, 1, bot, ct);
    }

    public async Task DrawMenuPopupAsync(string title, string[] items, int selectedIdx, CancellationToken ct)
    {
        var maxItemLen = items.Max(i => i.Length);
        var popupW = Math.Max(title.Length + 2, maxItemLen + 4);
        var popupH = items.Length + 2;
        const int popupX = 2;
        const int popupY = 3;

        await DrawPopupBorderAsync(popupX, popupY, popupW, popupH, ct);

        await DrawUniformRowAsync(popupX + 2, popupY + 1, title.Length,
            LowercaseScreenCodeConverter.Encode(title), _theme.PopupFg, ct);

        var sep = new string('-', popupW);
        await DrawUniformRowAsync(popupX + 1, popupY + 2, popupW,
            LowercaseScreenCodeConverter.Encode(sep), _theme.PopupFg, ct);

        for (var i = 0; i < items.Length; i++)
        {
            var rowY = popupY + 3 + i;
            var display = items[i].PadRight(popupW - 2);
            var color = i == selectedIdx ? _theme.Highlight : _theme.PopupFg;
            await DrawUniformRowAsync(popupX + 2, rowY, popupW - 2,
                LowercaseScreenCodeConverter.Encode(display), color, ct);
        }
    }

    public async Task UpdateMenuSelectionAsync(string title, string[] items, int oldIdx, int newIdx, CancellationToken ct)
    {
        var maxItemLen = items.Max(i => i.Length);
        var popupW = Math.Max(title.Length + 2, maxItemLen + 4);
        const int popupX = 2;
        const int popupY = 3;

        var oldDisplay = items[oldIdx].PadRight(popupW - 2);
        await DrawUniformRowAsync(popupX + 2, popupY + 3 + oldIdx, popupW - 2,
            LowercaseScreenCodeConverter.Encode(oldDisplay), _theme.PopupFg, ct);

        var newDisplay = items[newIdx].PadRight(popupW - 2);
        await DrawUniformRowAsync(popupX + 2, popupY + 3 + newIdx, popupW - 2,
            LowercaseScreenCodeConverter.Encode(newDisplay), _theme.Highlight, ct);
    }

    public async Task DrawInputDialogAsync(string title, string inputText, CancellationToken ct, bool fullRedraw = true)
    {
        const int popupW = 28;
        const int popupH = 3;
        const int popupX = 5;
        const int popupY = 10;

        if (fullRedraw)
        {
            await DrawPopupBorderAsync(popupX, popupY, popupW, popupH, ct);

            var titleDisplay = title[..Math.Min(title.Length, popupW - 2)];
            await DrawUniformRowAsync(popupX + 2, popupY + 1, titleDisplay.Length,
                LowercaseScreenCodeConverter.Encode(titleDisplay), _theme.PopupFg, ct);

            var hint = "return=ok  stop=cancel";
            await DrawUniformRowAsync(popupX + 2, popupY + 3, hint.Length,
                LowercaseScreenCodeConverter.EncodeCached(hint), _theme.PopupFg, ct);
        }

        // Always update the text field
        var inputDisplay = inputText.PadRight(popupW - 2)[..(popupW - 2)];
        await DrawUniformRowAsync(popupX + 2, popupY + 2, popupW - 2,
            LowercaseScreenCodeConverter.Encode(inputDisplay), _theme.Highlight, ct);
    }

    public async Task DrawConfirmDialogAsync(string message, CancellationToken ct)
    {
        var popupW = Math.Max(message.Length + 4, 22);
        const int popupH = 3;
        const int popupX = 5;
        const int popupY = 10;

        await DrawPopupBorderAsync(popupX, popupY, popupW, popupH, ct);

        await DrawUniformRowAsync(popupX + 2, popupY + 1, message.Length,
            LowercaseScreenCodeConverter.Encode(message), _theme.PopupFg, ct);

        var hint = "y = yes   n = no";
        await DrawUniformRowAsync(popupX + 2, popupY + 3, hint.Length,
            LowercaseScreenCodeConverter.EncodeCached(hint), _theme.PopupFg, ct);
    }

    public async Task DrawHelpScreenAsync(CancellationToken ct)
    {
        const int popupW = 34;
        const int popupH = 16;
        const int popupX = 2;
        const int popupY = 3;

        await DrawPopupBorderAsync(popupX, popupY, popupW, popupH, ct);

        string[] helpLines =
        [
            "rift writer - key reference",
            new('-', 32),
            "f1        file menu",
            "f3        edit menu",
            "f5        view menu",
            "f7        this help",
            "crsr      navigate",
            "home      start of line",
            "clr       start of doc",
            "del       delete left",
            "inst      toggle ins/ovr",
            "tab       insert 4 spaces",
            "return    new line",
            "view>spell check  spelling",
            new('-', 32),
            "press any key to close",
        ];

        for (var i = 0; i < helpLines.Length; i++)
        {
            var display = helpLines[i][..Math.Min(helpLines[i].Length, popupW - 2)];
            await DrawUniformRowAsync(popupX + 2, popupY + 1 + i, display.Length,
                LowercaseScreenCodeConverter.EncodeCached(display), _theme.PopupFg, ct);
        }
    }

    public async Task DrawFileBrowserAsync(string[] files, int selectedIdx, int scrollOffset, CancellationToken ct)
    {
        const int popupW = 28;
        const int maxVisible = 11;
        var popupH = maxVisible + 2;
        const int popupX = 5;
        const int popupY = 4;

        await DrawPopupBorderAsync(popupX, popupY, popupW, popupH, ct);

        var title = "open file";
        await DrawUniformRowAsync(popupX + 2, popupY + 1, title.Length,
            LowercaseScreenCodeConverter.EncodeCached(title), _theme.PopupFg, ct);

        var sep = new string('-', popupW - 2);
        await DrawUniformRowAsync(popupX + 1, popupY + 2, popupW,
            LowercaseScreenCodeConverter.Encode("-" + sep + "-"), _theme.PopupFg, ct);

        for (var i = 0; i < maxVisible; i++)
        {
            var fileIdx = scrollOffset + i;
            var rowY = popupY + 3 + i;
            if (fileIdx < files.Length)
            {
                var display = files[fileIdx].PadRight(popupW - 2)[..(popupW - 2)];
                var color = fileIdx == selectedIdx ? _theme.Highlight : _theme.PopupFg;
                await DrawUniformRowAsync(popupX + 2, rowY, popupW - 2,
                    LowercaseScreenCodeConverter.Encode(display), color, ct);
            }
            else
            {
                var blank = new string(' ', popupW - 2);
                await DrawUniformRowAsync(popupX + 2, rowY, popupW - 2,
                    LowercaseScreenCodeConverter.Encode(blank), _theme.PopupFg, ct);
            }
        }
    }

    public async Task UpdateFileBrowserSelectionAsync(string[] files, int oldIdx, int newIdx, int scrollOffset, CancellationToken ct)
    {
        const int popupW = 28;
        const int popupX = 5;
        const int popupY = 4;

        var oldVisual = oldIdx - scrollOffset;
        if (oldVisual >= 0 && oldVisual < 11)
        {
            var display = files[oldIdx].PadRight(popupW - 2)[..(popupW - 2)];
            await DrawUniformRowAsync(popupX + 2, popupY + 3 + oldVisual, popupW - 2,
                LowercaseScreenCodeConverter.Encode(display), _theme.PopupFg, ct);
        }

        var newVisual = newIdx - scrollOffset;
        if (newVisual >= 0 && newVisual < 11)
        {
            var display = files[newIdx].PadRight(popupW - 2)[..(popupW - 2)];
            await DrawUniformRowAsync(popupX + 2, popupY + 3 + newVisual, popupW - 2,
                LowercaseScreenCodeConverter.Encode(display), _theme.Highlight, ct);
        }
    }

    private async Task DrawUniformRowAsync(int x, int y, int width, byte[] screenCodes, byte color, CancellationToken ct)
    {
        await _client.SetCursorAsync((byte)x, (byte)y, ct);
        await _client.DrawColoredWindowRawAsync((Rift64Color)color, (byte)width, 1, screenCodes, ct);
    }

    public async Task DrawSpellPopupAsync(string word, string suggestion, int idx, int total, int wordScreenRow, CancellationToken ct)
    {
        const int popupW = 34;
        const int popupH = 5;
        const int popupX = 2;
        // Position popup away from the highlighted word
        var popupY = wordScreenRow <= 12 ? 16 : 2;

        await DrawPopupBorderAsync(popupX, popupY, popupW, popupH, ct);

        var title = $"spell {idx + 1}/{total}".PadRight(popupW - 2);
        await DrawUniformRowAsync(popupX + 2, popupY + 1, popupW - 2,
            LowercaseScreenCodeConverter.Encode(title), _theme.PopupFg, ct);

        var wordLine = $"Word: {word}"[..Math.Min($"Word: {word}".Length, popupW - 2)].PadRight(popupW - 2);
        await DrawUniformRowAsync(popupX + 2, popupY + 2, popupW - 2,
            LowercaseScreenCodeConverter.Encode(wordLine), 2, ct); // red

        var fixText = string.IsNullOrEmpty(suggestion) ? "(no suggestion)" : suggestion;
        var fixLine = $"fix:  {fixText}"[..Math.Min($"fix:  {fixText}".Length, popupW - 2)].PadRight(popupW - 2);
        await DrawUniformRowAsync(popupX + 2, popupY + 3, popupW - 2,
            LowercaseScreenCodeConverter.Encode(fixLine), _theme.Highlight, ct);

        var hint = "n=next p=prev r=replace x=exit";
        await DrawUniformRowAsync(popupX + 2, popupY + 4, popupW - 2,
            LowercaseScreenCodeConverter.EncodeCached(hint.PadRight(popupW - 2)), _theme.PopupFg, ct);
    }

    public async Task DrawFindReplacePopupAsync(int matchIdx, int totalMatches, int screenRow, CancellationToken ct)
    {
        const int popupW = 34;
        const int popupH = 5;
        const int popupX = 3;
        // If match is at the bottom, show popup at the top; else at the bottom
        var popupY = screenRow >= 12 ? 3 : 17;

        await DrawPopupBorderAsync(popupX, popupY, popupW, popupH, ct);

        var title = $"match {matchIdx + 1}/{totalMatches}";
        var paddedTitle = PadCenter(title, popupW - 4);
        await DrawUniformRowAsync(popupX + 2, popupY + 1, paddedTitle.Length,
            LowercaseScreenCodeConverter.Encode(paddedTitle), _theme.PopupFg, ct);

        var hint = "r:rep  n:next  a:all  x:exit";
        var paddedHint = PadCenter(hint, popupW - 4);
        await DrawUniformRowAsync(popupX + 2, popupY + 3, paddedHint.Length,
            LowercaseScreenCodeConverter.Encode(paddedHint), _theme.PopupFg, ct);
    }

    private static string PadCenter(string text, int width)
    {
        if (text.Length >= width) return text[..width];
        var left = (width - text.Length) / 2;
        return text.PadLeft(left + text.Length).PadRight(width);
    }
}
