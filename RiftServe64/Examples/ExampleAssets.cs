/// <summary>
/// Locates demo asset files relative to the running binary so the examples
/// work whether launched from <c>bin/Debug/...</c>, the project root, or
/// the repository root.
/// </summary>
public static class ExampleAssets
{
    private static readonly string[] SearchRoots =
    {
        "",
        "..",
        "../..",
        "../../..",
        "riftserve64",
    };

    /// <summary>
    /// Try to locate <paramref name="relativePath"/> by probing the standard
    /// search roots. Returns the absolute path on success or <c>null</c>.
    /// </summary>
    public static string? Find(string relativePath)
    {
        foreach (var root in SearchRoots)
        {
            var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// Locate a directory (e.g. the <c>pkg/</c> asset folder) anywhere on
    /// the search path.
    /// </summary>
    public static string? FindDirectory(string relativeDir)
    {
        foreach (var root in SearchRoots)
        {
            var candidate = Path.GetFullPath(Path.Combine(root, relativeDir));
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }
}
