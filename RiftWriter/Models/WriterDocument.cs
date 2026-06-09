using System.Text.Json;

namespace RiftWriter.Models;

/// <summary>
/// Line-based document with parallel color arrays.
/// </summary>
internal sealed class WriterDocument
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public List<string> Lines { get; set; } = [""];
    public List<List<byte>> Colors { get; set; } = [[]];
    public string Filename { get; set; } = "untitled.txt";
    public bool Modified { get; set; }
    public byte DefaultColor { get; set; } = (byte)RiftServe64.Sdk.Protocol.Rift64Color.LightGreen;

    public int TotalLines() => Lines.Count;

    public int LineLen(int row)
    {
        if (row >= 0 && row < Lines.Count)
            return Lines[row].Length;
        return 0;
    }

    /// <summary>
    /// Ensure Colors array is in sync with Lines (fixes any length mismatches).
    /// </summary>
    public void EnsureColorSync()
    {
        while (Colors.Count < Lines.Count)
            Colors.Add(Enumerable.Repeat(DefaultColor, Lines[Colors.Count].Length).ToList());
        while (Colors.Count > Lines.Count)
            Colors.RemoveAt(Colors.Count - 1);
        for (var i = 0; i < Lines.Count; i++)
        {
            var diff = Lines[i].Length - Colors[i].Count;
            if (diff > 0)
                Colors[i].AddRange(Enumerable.Repeat(DefaultColor, diff));
            else if (diff < 0)
                Colors[i].RemoveRange(Lines[i].Length, -diff);
        }
    }

    public void InsertChar(int row, int col, char ch, byte? color = null)
    {
        color ??= DefaultColor;
        if (row >= Lines.Count) return;
        var line = Lines[row];
        var cols = Colors[row];
        if (line.Length >= WriterConstants.DocMaxCols) return;
        Lines[row] = line[..col] + ch + line[col..];
        Colors[row] = [.. cols[..col], color.Value, .. cols[col..]];
        Modified = true;
    }

    /// <summary>
    /// Delete char at (row, col). Returns true if line merge happened.
    /// </summary>
    public bool DeleteChar(int row, int col)
    {
        if (row >= Lines.Count) return false;
        var line = Lines[row];
        if (col < line.Length)
        {
            Lines[row] = line[..col] + line[(col + 1)..];
            Colors[row] = [.. Colors[row][..col], .. Colors[row][(col + 1)..]];
            Modified = true;
            return false;
        }
        else if (row < Lines.Count - 1)
        {
            var nextLine = Lines[row + 1];
            var nextColors = Colors[row + 1];
            if (line.Length + nextLine.Length <= WriterConstants.DocMaxCols)
            {
                Lines[row] = line + nextLine;
                Colors[row] = [.. Colors[row], .. nextColors];
                Lines.RemoveAt(row + 1);
                Colors.RemoveAt(row + 1);
                Modified = true;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Delete char before cursor. Returns (newRow, newCol, merged).
    /// </summary>
    public (int Row, int Col, bool Merged) Backspace(int row, int col)
    {
        if (col > 0)
        {
            DeleteChar(row, col - 1);
            return (row, col - 1, false);
        }
        else if (row > 0)
        {
            var newCol = Lines[row - 1].Length;
            var prevLine = Lines[row - 1];
            var prevColors = Colors[row - 1];
            var curLine = Lines[row];
            var curColors = Colors[row];
            if (prevLine.Length + curLine.Length <= WriterConstants.DocMaxCols)
            {
                Lines[row - 1] = prevLine + curLine;
                Colors[row - 1] = [.. prevColors, .. curColors];
                Lines.RemoveAt(row);
                Colors.RemoveAt(row);
                Modified = true;
                return (row - 1, newCol, true);
            }
        }
        return (row, col, false);
    }

    /// <summary>
    /// Split line at cursor (Enter key). Returns new cursor position.
    /// </summary>
    public (int Row, int Col) SplitLine(int row, int col)
    {
        if (row >= Lines.Count) return (row, col);
        var line = Lines[row];
        var colors = Colors[row];
        var left = line[..col];
        var right = line[col..];
        var leftColors = colors[..col];
        var rightColors = colors[col..];
        Lines[row] = left;
        Colors[row] = [.. leftColors];
        Lines.Insert(row + 1, right);
        Colors.Insert(row + 1, [.. rightColors]);
        Modified = true;
        return (row + 1, 0);
    }

    /// <summary>
    /// Delete entire line. Returns deleted content for clipboard.
    /// </summary>
    public (string Text, List<byte> LineColors) DeleteLine(int row)
    {
        if (row >= Lines.Count) return ("", []);
        if (Lines.Count == 1)
        {
            var content = Lines[0];
            var colors = new List<byte>(Colors[0]);
            Lines[0] = "";
            Colors[0] = [];
            Modified = true;
            return (content, colors);
        }
        var text = Lines[row];
        var lineColors = Colors[row];
        Lines.RemoveAt(row);
        Colors.RemoveAt(row);
        Modified = true;
        return (text, lineColors);
    }

    public void InsertLine(int row, string text, List<byte>? colors = null)
    {
        colors ??= Enumerable.Repeat(DefaultColor, text.Length).ToList();
        Lines.Insert(row, text[..Math.Min(text.Length, WriterConstants.DocMaxCols)]);
        Colors.Insert(row, colors[..Math.Min(colors.Count, WriterConstants.DocMaxCols)]);
        Modified = true;
    }

    public void DuplicateLine(int row)
    {
        if (row < Lines.Count)
            InsertLine(row + 1, Lines[row], new List<byte>(Colors[row]));
    }

    public string ToText()
    {
        return string.Join("\n", Lines);
    }

    public void FromText(string text)
    {
        Lines = text.Split('\n').ToList();
        if (Lines.Count == 0) Lines.Add("");
        for (var i = 0; i < Lines.Count; i++)
        {
            if (Lines[i].Length > WriterConstants.DocMaxCols)
                Lines[i] = Lines[i][..WriterConstants.DocMaxCols];
        }
        Colors = Lines.Select(l => Enumerable.Repeat(DefaultColor, l.Length).ToList()).ToList();
        Modified = false;
    }

    public string ToRift()
    {
        var obj = new RiftFileFormat
        {
            Version = 1,
            Lines = Lines,
            Colors = Colors
        };
        return JsonSerializer.Serialize(obj, JsonOptions);
    }

    public void FromRift(string data)
    {
        var obj = JsonSerializer.Deserialize<RiftFileFormat>(data, JsonOptions);
        Lines = obj?.Lines ?? [""];
        Colors = obj?.Colors ?? [[]];
        // Ensure parallel arrays match
        while (Colors.Count < Lines.Count)
        {
            Colors.Add(Enumerable.Repeat(DefaultColor, Lines[Colors.Count].Length).ToList());
        }
        Modified = false;
    }

    private sealed class RiftFileFormat
    {
        public int Version { get; set; }
        public List<string> Lines { get; set; } = [];
        public List<List<byte>> Colors { get; set; } = [];
    }
}
