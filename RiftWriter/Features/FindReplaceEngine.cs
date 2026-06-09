using RiftWriter.Models;

namespace RiftWriter.Features;

/// <summary>
/// Stateful find-and-replace engine. Holds query, replacement text, and match list.
/// </summary>
internal sealed class FindReplaceEngine
{
    public string FindQuery { get; set; } = "";
    public string ReplaceQuery { get; set; } = "";
    public List<(int Row, int Col)> Matches { get; private set; } = [];
    public int MatchIndex { get; set; }

    public void Search(WriterDocument doc)
    {
        Matches = FindAllMatches(doc, FindQuery);
        MatchIndex = 0;
    }

    public static List<(int Row, int Col)> FindAllMatches(WriterDocument doc, string query)
    {
        var matches = new List<(int, int)>();
        if (string.IsNullOrEmpty(query))
            return matches;

        var lowerQuery = query.ToLowerInvariant();
        for (var r = 0; r < doc.TotalLines(); r++)
        {
            var line = doc.Lines[r].ToLowerInvariant();
            var startCol = 0;
            while (true)
            {
                var idx = line.IndexOf(lowerQuery, startCol, StringComparison.Ordinal);
                if (idx < 0) break;
                matches.Add((r, idx));
                startCol = idx + lowerQuery.Length;
            }
        }
        return matches;
    }

    /// <summary>
    /// Replace the current match and advance to the next. Returns true if more matches remain.
    /// </summary>
    public bool ReplaceCurrent(WriterDocument doc)
    {
        if (Matches.Count == 0) return false;
        var (row, col) = Matches[MatchIndex];
        var line = doc.Lines[row];
        var colors = doc.Colors[row];
        var baseColor = col < colors.Count ? colors[col] : doc.DefaultColor;

        doc.Lines[row] = line[..col] + ReplaceQuery + line[(col + FindQuery.Length)..];
        var newColors = new List<byte>(colors.GetRange(0, col));
        for (var i = 0; i < ReplaceQuery.Length; i++)
            newColors.Add(baseColor);
        if (col + FindQuery.Length < colors.Count)
            newColors.AddRange(colors.GetRange(col + FindQuery.Length, colors.Count - (col + FindQuery.Length)));
        doc.Colors[row] = newColors;
        doc.Modified = true;

        var nextRow = row;
        var nextCol = col + ReplaceQuery.Length;

        // Re-search
        Search(doc);
        if (Matches.Count == 0) return false;

        // Find first match at or after replacement end
        var newIdx = -1;
        for (var i = 0; i < Matches.Count; i++)
        {
            var (r, c) = Matches[i];
            if (r > nextRow || (r == nextRow && c >= nextCol))
            {
                newIdx = i;
                break;
            }
        }
        MatchIndex = newIdx >= 0 ? newIdx : 0;
        return true;
    }

    /// <summary>
    /// Replace all occurrences. Returns the count of replacements made.
    /// </summary>
    public int ReplaceAll(WriterDocument doc)
    {
        var count = 0;
        for (var row = 0; row < doc.TotalLines(); row++)
        {
            var line = doc.Lines[row];
            var colors = doc.Colors[row];
            var startCol = 0;
            while (true)
            {
                var col = line.ToLowerInvariant().IndexOf(FindQuery.ToLowerInvariant(), startCol, StringComparison.Ordinal);
                if (col < 0) break;
                var baseColor = col < colors.Count ? colors[col] : doc.DefaultColor;
                line = line[..col] + ReplaceQuery + line[(col + FindQuery.Length)..];
                var newColors = new List<byte>(colors.GetRange(0, col));
                for (var i = 0; i < ReplaceQuery.Length; i++)
                    newColors.Add(baseColor);
                if (col + FindQuery.Length < colors.Count)
                    newColors.AddRange(colors.GetRange(col + FindQuery.Length, colors.Count - (col + FindQuery.Length)));
                colors = newColors;
                count++;
                startCol = col + ReplaceQuery.Length;
            }
            doc.Lines[row] = line;
            doc.Colors[row] = colors;
        }
        if (count > 0) doc.Modified = true;
        Matches = [];
        return count;
    }
}
