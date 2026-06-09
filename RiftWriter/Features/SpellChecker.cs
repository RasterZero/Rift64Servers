using RiftWriter.IO;
using RiftWriter.Models;

namespace RiftWriter.Features;

internal sealed class SpellChecker
{
    private HashSet<string> _dictionary = [];

    private static readonly HashSet<string> CommonShortWords =
    [
        "i", "a", "an", "am", "as", "at", "be", "by", "do", "go", "he", "if", "in",
        "is", "it", "me", "my", "no", "of", "on", "or", "so", "to", "up", "us", "we",
        "ok", "oh", "hi", "ha", "the", "and", "for", "are", "but", "not", "you",
        "all", "can", "had", "her", "was", "one", "our", "out", "its", "has", "his",
        "how", "did", "get", "got", "let", "say", "she", "too", "use"
    ];

    public bool IsLoaded => _dictionary.Count > 0;

    public bool Load()
    {
        var path = DocumentPaths.DictionaryCache;
        if (!File.Exists(path))
        {
            Console.WriteLine($"Dictionary not found: {path}");
            return false;
        }

        try
        {
            var text = File.ReadAllText(path);
            _dictionary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in text.Split('\n'))
            {
                var w = line.Trim();
                if (w.Length > 0 && w.All(char.IsLetter))
                    _dictionary.Add(w.ToLowerInvariant());
            }
            foreach (var w in CommonShortWords)
                _dictionary.Add(w);
            Console.WriteLine($"Dictionary loaded: {_dictionary.Count} words");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Dictionary load failed: {ex.Message}");
            return false;
        }
    }

    public List<(int Row, int Col, string Word)> FindErrors(WriterDocument doc)
    {
        if (_dictionary.Count == 0)
            return [];

        var errors = new List<(int, int, string)>();
        for (var row = 0; row < doc.TotalLines(); row++)
        {
            var line = doc.Lines[row];
            var i = 0;
            var n = line.Length;
            while (i < n)
            {
                if (char.IsLetter(line[i]))
                {
                    var start = i;
                    while (i < n && (char.IsLetter(line[i]) || line[i] == '\''))
                        i++;
                    var word = line[start..i];
                    var core = word.Trim('\'').ToLowerInvariant();
                    if (core.Length > 0 && !_dictionary.Contains(core))
                        errors.Add((row, start, word));
                }
                else
                {
                    i++;
                }
            }
        }
        return errors;
    }

    public string Suggest(string word)
    {
        if (_dictionary.Count == 0)
            return "";

        var target = word.ToLowerInvariant();
        string best = "";
        var bestScore = 0.0;

        foreach (var candidate in _dictionary)
        {
            if (Math.Abs(candidate.Length - target.Length) > 2)
                continue;
            var score = Similarity(target, candidate);
            if (score > bestScore && score >= 0.7)
            {
                bestScore = score;
                best = candidate;
            }
        }
        return best;
    }

    private static double Similarity(string a, string b)
    {
        var maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0) return 1.0;
        var dist = LevenshteinDistance(a, b);
        return 1.0 - (double)dist / maxLen;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        var d = new int[n + 1, m + 1];

        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }
}
