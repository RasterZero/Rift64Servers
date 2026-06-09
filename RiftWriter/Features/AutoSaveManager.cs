using RiftWriter.IO;
using RiftWriter.Models;

namespace RiftWriter.Features;

/// <summary>
/// Auto-saves document after every 50 keystrokes or 120 seconds of inactivity.
/// Saves to "autosave_{filename}" without changing document name/modified state.
/// </summary>
internal sealed class AutoSaveManager
{
    private const int KeystrokeThreshold = 50;
    private const int TimeThresholdSeconds = 120;

    private int _keystrokesSinceSave;
    private DateTime _lastSaveTime = DateTime.UtcNow;

    public bool ShouldTrigger { get; private set; }

    public void RecordKeystroke()
    {
        _keystrokesSinceSave++;
    }

    public void Check(WriterDocument doc)
    {
        ShouldTrigger = false;
        if (!doc.Modified) return;

        var byKeystrokes = _keystrokesSinceSave >= KeystrokeThreshold;
        var byTime = (DateTime.UtcNow - _lastSaveTime).TotalSeconds >= TimeThresholdSeconds;

        if (byKeystrokes || byTime)
        {
            ShouldTrigger = true;
        }
    }

    public void Execute(WriterDocument doc)
    {
        var originalFilename = doc.Filename;
        var originalModified = doc.Modified;

        var autoFilename = originalFilename.StartsWith("autosave_")
            ? originalFilename
            : $"autosave_{originalFilename}";

        doc.Filename = autoFilename;
        DocumentFileManager.Save(doc);
        doc.Filename = originalFilename;
        doc.Modified = originalModified;

        _keystrokesSinceSave = 0;
        _lastSaveTime = DateTime.UtcNow;
        Console.WriteLine($"AutoSave: {autoFilename}");
    }

    public void Reset()
    {
        _keystrokesSinceSave = 0;
        _lastSaveTime = DateTime.UtcNow;
    }
}
