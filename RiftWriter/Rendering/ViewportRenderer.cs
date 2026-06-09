using RiftServe64.Sdk.Protocol;
using RiftWriter.Models;

namespace RiftWriter.Rendering;

/// <summary>
/// Differential viewport renderer.
/// </summary>
internal sealed class ViewportRenderer
{
    private readonly Rift64ProtocolClient _client;
    private readonly ThemeContext _theme;

    // Cache: per-row (screenCodes, colors), length == VIEWPORT_ROWS
    private (byte[] Text, byte[] Colors)[] _cache;

    public ViewportRenderer(Rift64ProtocolClient client, ThemeContext theme)
    {
        _client = client;
        _theme = theme;
        _cache = new (byte[], byte[])[WriterConstants.ViewportRows];
        ClearCache();
    }

    public void ClearCache()
    {
        for (var i = 0; i < WriterConstants.ViewportRows; i++)
            _cache[i] = (Array.Empty<byte>(), Array.Empty<byte>());
    }

    public (byte[] Text, byte[] Colors) GetRowPaddedData(WriterDocument doc, int docRow, int viewportX)
    {
        string line;
        List<byte> lineColors;
        if (docRow < doc.TotalLines())
        {
            line = doc.Lines[docRow];
            lineColors = doc.Colors[docRow];
        }
        else
        {
            line = "";
            lineColors = [];
        }

        // Slice the viewport window
        var sliceStart = Math.Min(viewportX, line.Length);
        var sliceEnd = Math.Min(viewportX + WriterConstants.ViewportCols, line.Length);
        var visibleText = line[sliceStart..sliceEnd].PadRight(WriterConstants.ViewportCols);

        var paddedColors = new byte[WriterConstants.ViewportCols];
        for (var i = 0; i < WriterConstants.ViewportCols; i++)
        {
            var srcIdx = viewportX + i;
            paddedColors[i] = srcIdx < lineColors.Count ? lineColors[srcIdx] : _theme.EditorFg;
        }

        var screenCodes = LowercaseScreenCodeConverter.Encode(visibleText);
        return (screenCodes, paddedColors);
    }

    public async Task RenderSingleRowAsync(WriterDocument doc, int docRow, int screenRow, int viewportX, CancellationToken ct)
    {
        var (sc, colors) = GetRowPaddedData(doc, docRow, viewportX);
        var first = colors[0];
        if (colors.All(c => c == first))
        {
            await _client.SetCursorAsync(0, (byte)screenRow, ct);
            await _client.DrawColoredWindowRawAsync((Rift64Color)first, WriterConstants.ViewportCols, 1, sc, ct);
        }
        else
        {
            await _client.SetCursorAsync(0, (byte)screenRow, ct);
            await _client.DrawWindowRawAsync(WriterConstants.ViewportCols, 1, sc, ct);
            await _client.FillColorBlockAsync(0, (byte)screenRow, WriterConstants.ViewportCols, 1, colors, ct);
        }
    }

    public async Task ScrollInsertRowAsync(WriterDocument doc, int docRow, int viewportY, int viewportX, CancellationToken ct)
    {
        var screenRow = docRow - viewportY + WriterConstants.ViewportTop;
        if (screenRow >= WriterConstants.ViewportTop && screenRow <= WriterConstants.ViewportBot)
        {
            var scrollHeight = WriterConstants.ViewportBot - screenRow + 1;
            await _client.ScrollRegionAsync(0, (byte)screenRow, WriterConstants.ViewportCols, (byte)scrollHeight, Rift64ScrollDirection.Down, ct);
            await RenderSingleRowAsync(doc, docRow, screenRow, viewportX, ct);

            // Shift cache
            var yOffset = screenRow - WriterConstants.ViewportTop;
            var newEntry = GetRowPaddedData(doc, docRow, viewportX);
            var newCache = new (byte[], byte[])[WriterConstants.ViewportRows];
            for (var i = 0; i < yOffset; i++) newCache[i] = _cache[i];
            newCache[yOffset] = newEntry;
            for (var i = yOffset; i < WriterConstants.ViewportRows - 1; i++) newCache[i + 1] = _cache[i];
            _cache = newCache;
        }
    }

    public async Task ScrollDeleteRowAsync(WriterDocument doc, int docRow, int viewportY, int viewportX, CancellationToken ct)
    {
        var screenRow = docRow - viewportY + WriterConstants.ViewportTop;
        if (screenRow >= WriterConstants.ViewportTop && screenRow <= WriterConstants.ViewportBot)
        {
            var scrollHeight = WriterConstants.ViewportBot - screenRow + 1;
            await _client.ScrollRegionAsync(0, (byte)screenRow, WriterConstants.ViewportCols, (byte)scrollHeight, Rift64ScrollDirection.Up, ct);

            var bottomDocRow = viewportY + WriterConstants.ViewportRows - 1;
            await RenderSingleRowAsync(doc, bottomDocRow, WriterConstants.ViewportBot, viewportX, ct);

            // Shift cache
            var yOffset = screenRow - WriterConstants.ViewportTop;
            var newCache = new (byte[], byte[])[WriterConstants.ViewportRows];
            var dst = 0;
            for (var i = 0; i < WriterConstants.ViewportRows; i++)
            {
                if (i == yOffset) continue;
                if (dst < WriterConstants.ViewportRows) newCache[dst++] = _cache[i];
            }
            newCache[WriterConstants.ViewportRows - 1] = GetRowPaddedData(doc, bottomDocRow, viewportX);
            _cache = newCache;
        }
    }

    public async Task<(int ViewportY, int ViewportX)> RenderAsync(
        WriterDocument doc,
        int cursorRow, int cursorCol,
        int viewportY, int viewportX,
        int? newViewportY = null,
        int? newViewportX = null,
        bool forceRedraw = false,
        CancellationToken ct = default)
    {
        await _client.SetCursorVisibilityAsync(false, ct);

        var targetY = newViewportY ?? viewportY;
        var targetX = newViewportX ?? viewportX;

        var dy = targetY - viewportY;
        var dx = targetX - viewportX;

        var cacheValid = _cache[0].Text.Length > 0;

        // --- Pure vertical hardware scroll ---
        if (!forceRedraw && dy != 0 && dx == 0 && Math.Abs(dy) <= WriterConstants.VScrollMax && cacheValid)
        {
            var n = Math.Abs(dy);
            var direction = dy > 0 ? Rift64ScrollDirection.Up : Rift64ScrollDirection.Down;
            for (var i = 0; i < n; i++)
                await _client.ScrollRegionAsync(0, WriterConstants.ViewportTop, WriterConstants.ViewportCols, WriterConstants.ViewportRows, direction, ct);

            viewportY = targetY;
            var visibleRows = new (byte[] Text, byte[] Colors)[WriterConstants.ViewportRows];
            for (var yOff = 0; yOff < WriterConstants.ViewportRows; yOff++)
                visibleRows[yOff] = GetRowPaddedData(doc, viewportY + yOff, viewportX);

            int edgeY;
            (byte[] Text, byte[] Colors)[] edgeRows;
            if (dy > 0)
            {
                edgeRows = visibleRows[(WriterConstants.ViewportRows - n)..];
                edgeY = WriterConstants.ViewportBot - n + 1;
            }
            else
            {
                edgeRows = visibleRows[..n];
                edgeY = WriterConstants.ViewportTop;
            }

            await WriteEdgeBlockAsync(edgeRows, 0, edgeY, WriterConstants.ViewportCols, n, ct);
            _cache = visibleRows;
            return (viewportY, viewportX);
        }

        // --- Pure horizontal hardware scroll ---
        if (!forceRedraw && dx != 0 && dy == 0 && Math.Abs(dx) <= WriterConstants.HScrollMax && cacheValid)
        {
            var n = Math.Abs(dx);
            var direction = dx > 0 ? Rift64ScrollDirection.Left : Rift64ScrollDirection.Right;
            for (var i = 0; i < n; i++)
                await _client.ScrollRegionAsync(0, WriterConstants.ViewportTop, WriterConstants.ViewportCols, WriterConstants.ViewportRows, direction, ct);

            viewportX = targetX;
            var visibleRows = new (byte[] Text, byte[] Colors)[WriterConstants.ViewportRows];
            for (var yOff = 0; yOff < WriterConstants.ViewportRows; yOff++)
                visibleRows[yOff] = GetRowPaddedData(doc, viewportY + yOff, viewportX);

            // Build edge strip
            int edgeX;
            var edgeText = new byte[n * WriterConstants.ViewportRows];
            var edgeCols = new byte[n * WriterConstants.ViewportRows];
            if (dx > 0)
            {
                edgeX = WriterConstants.ViewportCols - n;
                for (var r = 0; r < WriterConstants.ViewportRows; r++)
                {
                    Array.Copy(visibleRows[r].Text, WriterConstants.ViewportCols - n, edgeText, r * n, n);
                    Array.Copy(visibleRows[r].Colors, WriterConstants.ViewportCols - n, edgeCols, r * n, n);
                }
            }
            else
            {
                edgeX = 0;
                for (var r = 0; r < WriterConstants.ViewportRows; r++)
                {
                    Array.Copy(visibleRows[r].Text, 0, edgeText, r * n, n);
                    Array.Copy(visibleRows[r].Colors, 0, edgeCols, r * n, n);
                }
            }

            var first = edgeCols[0];
            if (edgeCols.All(c => c == first))
            {
                await _client.SetCursorAsync((byte)edgeX, WriterConstants.ViewportTop, ct);
                await _client.DrawColoredWindowRawAsync((Rift64Color)first, (byte)n, WriterConstants.ViewportRows, edgeText, ct);
            }
            else
            {
                await _client.SetCursorAsync((byte)edgeX, WriterConstants.ViewportTop, ct);
                await _client.DrawWindowRawAsync((byte)n, WriterConstants.ViewportRows, edgeText, ct);
                await _client.FillColorBlockAsync((byte)edgeX, WriterConstants.ViewportTop, (byte)n, WriterConstants.ViewportRows, edgeCols, ct);
            }

            // Refresh cursor row (fixes right-edge typing bug)
            var cursorScreenRow = cursorRow - viewportY + WriterConstants.ViewportTop;
            if (cursorScreenRow >= WriterConstants.ViewportTop && cursorScreenRow <= WriterConstants.ViewportBot)
            {
                var rowIdx = cursorRow - viewportY;
                if (rowIdx >= 0 && rowIdx < WriterConstants.ViewportRows)
                {
                    var (rowSc, rowCols) = visibleRows[rowIdx];
                    var fc = rowCols[0];
                    if (rowCols.All(c => c == fc))
                    {
                        await _client.SetCursorAsync(0, (byte)cursorScreenRow, ct);
                        await _client.DrawColoredWindowRawAsync((Rift64Color)fc, WriterConstants.ViewportCols, 1, rowSc, ct);
                    }
                    else
                    {
                        await _client.SetCursorAsync(0, (byte)cursorScreenRow, ct);
                        await _client.DrawWindowRawAsync(WriterConstants.ViewportCols, 1, rowSc, ct);
                        await _client.FillColorBlockAsync(0, (byte)cursorScreenRow, WriterConstants.ViewportCols, 1, rowCols, ct);
                    }
                }
            }

            _cache = visibleRows;
            return (viewportY, viewportX);
        }

        // --- Fallback: differential or full redraw ---
        if (forceRedraw)
            ClearCache();

        viewportY = targetY;
        viewportX = targetX;

        var rows = new (byte[] Text, byte[] Colors)[WriterConstants.ViewportRows];
        for (var yOff = 0; yOff < WriterConstants.ViewportRows; yOff++)
            rows[yOff] = GetRowPaddedData(doc, viewportY + yOff, viewportX);

        if (forceRedraw || _cache[0].Text.Length == 0)
        {
            // Full redraw: all rows as W, then one big Q
            for (var yOff = 0; yOff < WriterConstants.ViewportRows; yOff++)
            {
                await _client.SetCursorAsync(0, (byte)(WriterConstants.ViewportTop + yOff), ct);
                await _client.DrawWindowRawAsync(WriterConstants.ViewportCols, 1, rows[yOff].Text, ct);
            }
            var allColors = new byte[WriterConstants.ViewportCols * WriterConstants.ViewportRows];
            for (var yOff = 0; yOff < WriterConstants.ViewportRows; yOff++)
                Array.Copy(rows[yOff].Colors, 0, allColors, yOff * WriterConstants.ViewportCols, WriterConstants.ViewportCols);
            await _client.FillColorBlockAsync(0, WriterConstants.ViewportTop, WriterConstants.ViewportCols, WriterConstants.ViewportRows, allColors, ct);
            _cache = rows;
        }
        else
        {
            // Differential row-by-row
            for (var yOff = 0; yOff < WriterConstants.ViewportRows; yOff++)
            {
                var (newText, newColors) = rows[yOff];
                var (cacheText, cacheColors) = _cache[yOff];

                var diffStart = -1;
                var diffEnd = -1;
                for (var i = 0; i < WriterConstants.ViewportCols; i++)
                {
                    var charChanged = i >= cacheText.Length || newText[i] != cacheText[i];
                    var colChanged = (i >= cacheColors.Length || newColors[i] != cacheColors[i]) && newText[i] != 0x20;
                    if (charChanged || colChanged) { diffStart = i; break; }
                }
                if (diffStart == -1) continue;

                for (var j = WriterConstants.ViewportCols - 1; j >= 0; j--)
                {
                    var charChanged = j >= cacheText.Length || newText[j] != cacheText[j];
                    var colChanged = (j >= cacheColors.Length || newColors[j] != cacheColors[j]) && newText[j] != 0x20;
                    if (charChanged || colChanged) { diffEnd = j; break; }
                }

                var diffLen = diffEnd - diffStart + 1;
                var diffSc = newText[diffStart..(diffEnd + 1)];
                var diffCols = newColors[diffStart..(diffEnd + 1)];
                var y = (byte)(WriterConstants.ViewportTop + yOff);

                var fc = diffCols[0];
                if (diffCols.All(c => c == fc))
                {
                    await _client.SetCursorAsync((byte)diffStart, y, ct);
                    await _client.DrawColoredWindowRawAsync((Rift64Color)fc, (byte)diffLen, 1, diffSc, ct);
                }
                else
                {
                    await _client.SetCursorAsync((byte)diffStart, y, ct);
                    await _client.DrawWindowRawAsync((byte)diffLen, 1, diffSc, ct);
                    await _client.FillColorBlockAsync((byte)diffStart, y, (byte)diffLen, 1, diffCols, ct);
                }
                _cache[yOff] = (newText, newColors);
            }
        }

        return (viewportY, viewportX);
    }

    private async Task WriteEdgeBlockAsync((byte[] Text, byte[] Colors)[] edgeRows, int x, int y, int width, int height, CancellationToken ct)
    {
        var totalCells = width * height;
        var edgeText = new byte[totalCells];
        var edgeCols = new byte[totalCells];
        for (var r = 0; r < height; r++)
        {
            Array.Copy(edgeRows[r].Text, 0, edgeText, r * width, width);
            Array.Copy(edgeRows[r].Colors, 0, edgeCols, r * width, width);
        }

        var first = edgeCols[0];
        if (edgeCols.All(c => c == first))
        {
            await _client.SetCursorAsync((byte)x, (byte)y, ct);
            await _client.DrawColoredWindowRawAsync((Rift64Color)first, (byte)width, (byte)height, edgeText, ct);
        }
        else
        {
            await _client.SetCursorAsync((byte)x, (byte)y, ct);
            await _client.DrawWindowRawAsync((byte)width, (byte)height, edgeText, ct);
            await _client.FillColorBlockAsync((byte)x, (byte)y, (byte)width, (byte)height, edgeCols, ct);
        }
    }
}
