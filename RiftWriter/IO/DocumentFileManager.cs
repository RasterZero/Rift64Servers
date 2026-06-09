using RiftWriter.Models;

namespace RiftWriter.IO;

/// <summary>
/// List, load, and save .txt/.rift documents. File format must match standard specifications.
/// </summary>
internal static class DocumentFileManager
{
    public static void EnsureDocumentsDir()
    {
        Directory.CreateDirectory(DocumentPaths.DocumentsDir);
    }

    public static List<string> ListDocuments()
    {
        EnsureDocumentsDir();
        var files = new List<string>();
        foreach (var f in Directory.EnumerateFiles(DocumentPaths.DocumentsDir)
                     .Select(Path.GetFileName)
                     .Where(n => n is not null)
                     .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var ext = Path.GetExtension(f)?.ToLowerInvariant();
            if (ext is ".txt" or ".rift")
                files.Add(f!);
        }
        return files;
    }

    public static void Save(WriterDocument doc)
    {
        EnsureDocumentsDir();
        var path = Path.Combine(DocumentPaths.DocumentsDir, doc.Filename);
        if (doc.Filename.EndsWith(".rift", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(path, doc.ToRift());
        }
        else
        {
            File.WriteAllText(path, doc.ToText());
        }
        doc.Modified = false;
    }

    public static void Load(WriterDocument doc, string filename)
    {
        var path = Path.Combine(DocumentPaths.DocumentsDir, filename);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Document not found: {filename}", path);

        var content = File.ReadAllText(path);
        if (filename.EndsWith(".rift", StringComparison.OrdinalIgnoreCase))
        {
            doc.FromRift(content);
        }
        else
        {
            doc.FromText(content);
        }
        doc.EnsureColorSync();
        doc.Filename = filename;
        doc.Modified = false;
    }
}
