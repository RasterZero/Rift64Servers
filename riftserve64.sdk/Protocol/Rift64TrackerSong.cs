using System;
using System.Collections.Generic;

namespace RiftServe64.Sdk.Protocol;

/// <summary>
/// SID note names and PAL frequency math, mirroring the client's note table
/// (tracker.asm). Note index 1 = C-0, index 95 = B-7; A-4 (index 58) = 440 Hz.
/// </summary>
public static class SidNote
{
    /// <summary>Note byte for a gate release in tracker rows.</summary>
    public const byte NoteOff = 0x60;

    private const double PalClock = 985248.0;

    /// <summary>
    /// Parses a note name like <c>"C-4"</c>, <c>"A#3"</c> or <c>"F#0"</c>
    /// into the client's note index (1-95). The middle character is either
    /// <c>#</c> (sharp) or <c>-</c> (natural); octaves run 0-7.
    /// </summary>
    public static byte Index(string name)
    {
        if (name is not { Length: 3 })
            throw new ArgumentException($"Note name must be 3 characters like \"C-4\" or \"A#3\" (got \"{name}\").", nameof(name));

        var semitone = char.ToUpperInvariant(name[0]) switch
        {
            'C' => 0,
            'D' => 2,
            'E' => 4,
            'F' => 5,
            'G' => 7,
            'A' => 9,
            'B' => 11,
            _ => throw new ArgumentException($"Unknown note letter '{name[0]}'.", nameof(name)),
        };

        switch (name[1])
        {
            case '#': semitone++; break;
            case '-': break;
            default: throw new ArgumentException($"Note accidental must be '#' or '-' (got '{name[1]}').", nameof(name));
        }

        if (name[2] is < '0' or > '7')
            throw new ArgumentException($"Note octave must be 0-7 (got '{name[2]}').", nameof(name));

        var index = (name[2] - '0') * 12 + semitone + 1;
        if (index > 95)
            throw new ArgumentException($"Note \"{name}\" is above B-7.", nameof(name));
        return (byte)index;
    }

    /// <summary>PAL SID oscillator value for a note index (1-95).</summary>
    public static ushort Frequency(byte noteIndex)
    {
        if (noteIndex is < 1 or > 95)
            throw new ArgumentOutOfRangeException(nameof(noteIndex), "Note index must be 1-95.");
        var hz = 440.0 * Math.Pow(2.0, (noteIndex - 58.0) / 12.0);
        return (ushort)Math.Round(hz * 16777216.0 / PalClock);
    }

    /// <summary>PAL SID oscillator value for a note name like <c>"C-4"</c>.</summary>
    public static ushort Frequency(string name) => Frequency(Index(name));
}

/// <summary>
/// One pattern: a fixed number of rows, each holding a note/instrument pair
/// per voice. Cells default to "no event".
/// </summary>
public sealed class TrackerPattern
{
    private readonly TrackerRow[] _rows;

    internal TrackerPattern(int rows)
    {
        _rows = new TrackerRow[rows];
    }

    public int Rows => _rows.Length;

    /// <summary>Note on, e.g. <c>SetNote(0, 0, "C-4", 1)</c>. Instrument 0 reuses the voice's previous one.</summary>
    public TrackerPattern SetNote(int row, int voice, string note, byte instrument = 0)
    {
        _rows[row].SetNote(voice, note, instrument);
        return this;
    }

    /// <summary>Note on by raw note index (1-95).</summary>
    public TrackerPattern SetNote(int row, int voice, byte noteIndex, byte instrument = 0)
    {
        _rows[row].SetNote(voice, noteIndex, instrument);
        return this;
    }

    /// <summary>Release the voice's gate at this row.</summary>
    public TrackerPattern SetNoteOff(int row, int voice)
    {
        _rows[row].SetNoteOff(voice);
        return this;
    }

    /// <summary>Trigger the SFX/drum script in slot 0-15 on this voice at this row.</summary>
    public TrackerPattern SetDrum(int row, int voice, byte sfxSlot)
    {
        _rows[row].SetDrum(voice, sfxSlot);
        return this;
    }

    /// <summary>Copy of this pattern's rows, e.g. for streaming via <see cref="Rift64ProtocolClient.StreamTrackerRowsAsync"/>.</summary>
    public TrackerRow[] ToRows() => (TrackerRow[])_rows.Clone();

    internal void WriteTo(Span<byte> destination)
    {
        for (var r = 0; r < _rows.Length; r++)
        {
            _rows[r].CopyTo(destination.Slice(r * 6, 6));
        }
    }
}

/// <summary>
/// A complete tracker song: speed, patterns and an orderlist.
/// <see cref="Compile"/> produces the client binary for a chosen upload
/// address (pattern pointers are absolute, baked by this encoder), ready for
/// <see cref="Rift64ProtocolClient.UploadSongAsync"/>.
/// </summary>
public sealed class TrackerSong
{
    public const int MaxOrders = 64;
    public const int MaxPatterns = 32;
    private const int HeaderSize = 132;

    private readonly List<TrackerPattern> _patterns = [];

    /// <param name="rowsPerPattern">Rows in every pattern of this song (typically 16, 32 or 64).</param>
    public TrackerSong(int rowsPerPattern = 32)
    {
        if (rowsPerPattern is < 1 or > 255)
            throw new ArgumentOutOfRangeException(nameof(rowsPerPattern), "Rows per pattern must be 1-255.");
        RowsPerPattern = rowsPerPattern;
    }

    public int RowsPerPattern { get; }

    /// <summary>Frames per row (1-31). 6 at 50 fps PAL is the classic feel.</summary>
    public byte Speed { get; set; } = 6;

    /// <summary>Orderlist index playback restarts at after the last entry.</summary>
    public byte LoopOrder { get; set; }

    /// <summary>Pattern indices in playback order.</summary>
    public List<int> Order { get; } = [];

    public IReadOnlyList<TrackerPattern> Patterns => _patterns;

    /// <summary>Adds a new empty pattern and returns it; its index is <c>Patterns.Count - 1</c>.</summary>
    public TrackerPattern AddPattern()
    {
        if (_patterns.Count >= MaxPatterns)
            throw new InvalidOperationException($"A song holds at most {MaxPatterns} patterns.");
        var pattern = new TrackerPattern(RowsPerPattern);
        _patterns.Add(pattern);
        return pattern;
    }

    /// <summary>
    /// Compiles the song into the client binary format for an upload at
    /// <paramref name="baseAddress"/> (>= $4000; the whole song must fit
    /// below $D000).
    /// </summary>
    public byte[] Compile(ushort baseAddress)
    {
        if (Speed is < 1 or > 31)
            throw new InvalidOperationException("Speed must be 1-31 frames per row.");
        if (Order.Count is < 1 or > MaxOrders)
            throw new InvalidOperationException($"Orderlist must have 1-{MaxOrders} entries.");
        if (_patterns.Count == 0)
            throw new InvalidOperationException("Song has no patterns.");
        if (LoopOrder >= Order.Count)
            throw new InvalidOperationException("LoopOrder must reference an orderlist entry.");
        foreach (var entry in Order)
        {
            if (entry < 0 || entry >= _patterns.Count)
                throw new InvalidOperationException($"Orderlist entry {entry} does not name a pattern.");
        }

        var patternSize = RowsPerPattern * 6;
        var total = HeaderSize + _patterns.Count * patternSize;
        if (baseAddress < 0x4000)
            throw new ArgumentOutOfRangeException(nameof(baseAddress), "Songs live in the server upload zone ($4000+).");
        if (baseAddress + total > 0xD000)
            throw new InvalidOperationException($"Song ({total} bytes at ${baseAddress:X4}) would cross $D000.");

        var data = new byte[total];
        data[0] = Speed;
        data[1] = (byte)Order.Count;
        data[2] = LoopOrder;
        data[3] = (byte)RowsPerPattern;

        for (var i = 0; i < Order.Count; i++)
        {
            data[4 + i] = (byte)Order[i];
        }

        for (var p = 0; p < _patterns.Count; p++)
        {
            var address = baseAddress + HeaderSize + p * patternSize;
            data[68 + p] = (byte)(address & 0xFF);
            data[100 + p] = (byte)(address >> 8);
            _patterns[p].WriteTo(data.AsSpan(HeaderSize + p * patternSize, patternSize));
        }

        return data;
    }
}
