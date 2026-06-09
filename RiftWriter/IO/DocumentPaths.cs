namespace RiftWriter.IO;

/// <summary>
/// Resolves document and dictionary file paths.
/// </summary>
internal static class DocumentPaths
{
    private static string? _documentsDir;
    private static string? _dictionaryCache;

    public static string DocumentsDir
    {
        get
        {
            if (_documentsDir is null)
            {
                // Prefer repo-relative path so .rift files are shared with standard directories
                var repoRelative = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "test_scripts", "documents");
                if (Directory.Exists(repoRelative))
                {
                    _documentsDir = Path.GetFullPath(repoRelative);
                }
                else
                {
                    // Try working directory relative
                    var workingRelative = Path.Combine(Directory.GetCurrentDirectory(), "..", "test_scripts", "documents");
                    if (Directory.Exists(workingRelative))
                    {
                        _documentsDir = Path.GetFullPath(workingRelative);
                    }
                    else
                    {
                        // Fallback to exe-local
                        _documentsDir = Path.Combine(AppContext.BaseDirectory, "documents");
                    }
                }
            }
            return _documentsDir;
        }
    }

    public static string DictionaryCache
    {
        get
        {
            if (_dictionaryCache is null)
            {
                var repoRelative = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "test_scripts", "dictionary.txt");
                if (File.Exists(repoRelative))
                {
                    _dictionaryCache = Path.GetFullPath(repoRelative);
                }
                else
                {
                    var workingRelative = Path.Combine(Directory.GetCurrentDirectory(), "..", "test_scripts", "dictionary.txt");
                    _dictionaryCache = File.Exists(workingRelative)
                        ? Path.GetFullPath(workingRelative)
                        : Path.Combine(AppContext.BaseDirectory, "dictionary.txt");
                }
            }
            return _dictionaryCache;
        }
    }
}
