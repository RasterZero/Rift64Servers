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

    /// <summary>
    /// MiniPlayer2 modules are assembled with a 2-byte little-endian load
    /// address prefix (typically <c>$7000</c>). Strip exactly that header
    /// when present.
    /// </summary>
    public static byte[] StripMiniPlayer2Header(byte[] moduleBytes)
    {
        if (moduleBytes.Length < 2) return moduleBytes;
        // Load address $7000 → bytes 0x00, 0x70
        if (moduleBytes[0] == 0x00 && moduleBytes[1] == 0x70)
        {
            return moduleBytes[2..];
        }
        return moduleBytes;
    }
}
