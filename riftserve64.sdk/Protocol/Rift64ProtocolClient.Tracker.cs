using System;
using System.Threading;
using System.Threading.Tasks;

namespace RiftServe64.Sdk.Protocol;

/// <summary>
/// Tracker (pattern sequencer) API. The client runs one sequencer with two
/// feeds: a <b>local</b> song (compiled with <see cref="TrackerSong"/>,
/// uploaded into the $4000+ zone and played entirely client-side, immune to
/// network jitter) and a <b>remote</b> feed where the host streams rows live
/// (<see cref="StreamTrackerRowsAsync"/>) into a 32-row client-side ring
/// buffer consumed at row rate by the same decoder. Rows drive the
/// SoundBridge synth, so tracker notes use the same instruments (AI),
/// effects and SFX/drum scripts as the direct commands.
/// </summary>
public sealed partial class Rift64ProtocolClient
{
    // ------------------------------------------------------------------
    // Transport (digits; wire-compatible with the old MiniPlayer2 set)
    // ------------------------------------------------------------------

    /// <summary>
    /// Binds the song uploaded at <paramref name="address"/> (command
    /// <c>A5</c>). The address must point into the server upload zone
    /// (>= $4000). Stops any current playback and caches the song header.
    /// </summary>
    public Task<bool?> BindSongAsync(ushort address, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('5', new[] { (byte)(address & 0xFF), (byte)(address >> 8) }, timeout, cancellationToken);

    /// <inheritdoc cref="BindSongAsync(ushort,TimeSpan,CancellationToken)"/>
    public Task<bool?> BindSongAsync(ushort address, CancellationToken cancellationToken = default) =>
        BindSongAsync(address, DefaultAckTimeout, cancellationToken);

    /// <summary>
    /// Starts local playback of the bound song from an orderlist position
    /// (command <c>A1</c>). NAKs if no song is bound or the index is out of
    /// range.
    /// </summary>
    public Task<bool?> PlaySongAsync(byte startOrder, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('1', new[] { startOrder }, timeout, cancellationToken);

    /// <inheritdoc cref="PlaySongAsync(byte,TimeSpan,CancellationToken)"/>
    public Task<bool?> PlaySongAsync(byte startOrder = 0, CancellationToken cancellationToken = default) =>
        PlaySongAsync(startOrder, DefaultAckTimeout, cancellationToken);

    /// <summary>Stops the tracker and releases all voices (command <c>A0</c>).</summary>
    public Task<bool?> StopSongAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('0', ReadOnlyMemory<byte>.Empty, timeout, cancellationToken);

    /// <inheritdoc cref="StopSongAsync(TimeSpan,CancellationToken)"/>
    public Task<bool?> StopSongAsync(CancellationToken cancellationToken = default) =>
        StopSongAsync(DefaultAckTimeout, cancellationToken);

    /// <summary>
    /// Pauses the tracker (command <c>A2</c>). Position is kept and
    /// sustaining voices keep ringing; resume with <see cref="ResumeSongAsync(CancellationToken)"/>.
    /// </summary>
    public Task<bool?> PauseSongAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('2', ReadOnlyMemory<byte>.Empty, timeout, cancellationToken);

    /// <inheritdoc cref="PauseSongAsync(TimeSpan,CancellationToken)"/>
    public Task<bool?> PauseSongAsync(CancellationToken cancellationToken = default) =>
        PauseSongAsync(DefaultAckTimeout, cancellationToken);

    /// <summary>Resumes paused playback (command <c>A3</c>).</summary>
    public Task<bool?> ResumeSongAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('3', ReadOnlyMemory<byte>.Empty, timeout, cancellationToken);

    /// <inheritdoc cref="ResumeSongAsync(TimeSpan,CancellationToken)"/>
    public Task<bool?> ResumeSongAsync(CancellationToken cancellationToken = default) =>
        ResumeSongAsync(DefaultAckTimeout, cancellationToken);

    /// <summary>
    /// Sets the row tempo in frames per row, 1..31 (command <c>A4</c>).
    /// 50 fps PAL / speed = rows per second; speed 6 at 50 fps is the classic
    /// 125-BPM feel. Applies to both local and remote playback.
    /// </summary>
    public Task<bool?> SetSongSpeedAsync(byte framesPerRow, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('4', new[] { framesPerRow }, timeout, cancellationToken);

    /// <inheritdoc cref="SetSongSpeedAsync(byte,TimeSpan,CancellationToken)"/>
    public Task<bool?> SetSongSpeedAsync(byte framesPerRow, CancellationToken cancellationToken = default) =>
        SetSongSpeedAsync(framesPerRow, DefaultAckTimeout, cancellationToken);

    /// <summary>
    /// Jumps local playback to an orderlist position at the next row
    /// boundary (command <c>AJ</c>). Only valid while playing locally.
    /// </summary>
    public Task<bool?> JumpToOrderAsync(byte order, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('J', new[] { order }, timeout, cancellationToken);

    /// <inheritdoc cref="JumpToOrderAsync(byte,TimeSpan,CancellationToken)"/>
    public Task<bool?> JumpToOrderAsync(byte order, CancellationToken cancellationToken = default) =>
        JumpToOrderAsync(order, DefaultAckTimeout, cancellationToken);

    // ------------------------------------------------------------------
    // Remote (streamed-row) mode
    // ------------------------------------------------------------------

    /// <summary>
    /// Enters or exits remote tracker mode (command <c>AT</c>). Entering
    /// resets the client's row ring buffer and underrun/overrun counters and
    /// starts consuming streamed rows at the current speed; exiting performs
    /// a full tracker stop.
    /// </summary>
    public Task<bool?> SetTrackerRemoteModeAsync(bool enabled, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('T', new[] { (byte)(enabled ? 1 : 0) }, timeout, cancellationToken);

    /// <inheritdoc cref="SetTrackerRemoteModeAsync(bool,TimeSpan,CancellationToken)"/>
    public Task<bool?> SetTrackerRemoteModeAsync(bool enabled, CancellationToken cancellationToken = default) =>
        SetTrackerRemoteModeAsync(enabled, DefaultAckTimeout, cancellationToken);

    /// <summary>
    /// Streams tracker rows into the client's ring buffer (command <c>AU</c>,
    /// fire-and-forget). The client buffers up to 32 rows; rows that do not
    /// fit are dropped and counted as overruns. Use
    /// <see cref="QueryTrackerStatusAsync(CancellationToken)"/> to pace the
    /// feed (keep roughly 16-24 rows buffered).
    /// </summary>
    public async Task StreamTrackerRowsAsync(ReadOnlyMemory<TrackerRow> rows, CancellationToken cancellationToken = default)
    {
        var remaining = rows;
        while (remaining.Length > 0)
        {
            var batch = Math.Min(8, remaining.Length);
            var args = new byte[1 + batch * 6];
            args[0] = (byte)batch;
            for (var i = 0; i < batch; i++)
            {
                remaining.Span[i].CopyTo(args.AsSpan(1 + i * 6, 6));
            }

            await SendAudioStreamCommandAsync('U', args, cancellationToken).ConfigureAwait(false);
            remaining = remaining[batch..];
        }
    }

    /// <summary>
    /// Queries tracker status (command <c>AY</c>): playback state, orderlist
    /// position, row, buffered remote rows, and the underrun/overrun
    /// counters. Returns <c>null</c> on timeout.
    /// </summary>
    public async Task<TrackerStatus?> QueryTrackerStatusAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        await SendCommandAsync(CommandAudio, new[] { (byte)'Y' }, cancellationToken).ConfigureAwait(false);

        var buffer = new byte[5];
        var filled = 0;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            while (filled < buffer.Length)
            {
                var read = await _connection.ReadAsync(buffer.AsMemory(filled), timeoutSource.Token).ConfigureAwait(false);
                if (read <= 0) return null;
                filled += read;
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }

        return new TrackerStatus(
            (TrackerPlaybackState)buffer[0],
            Order: buffer[1],
            Row: buffer[2],
            BufferedRows: buffer[3],
            Underruns: (byte)(buffer[4] & 0x0F),
            Overruns: (byte)(buffer[4] >> 4));
    }

    /// <inheritdoc cref="QueryTrackerStatusAsync(TimeSpan,CancellationToken)"/>
    public Task<TrackerStatus?> QueryTrackerStatusAsync(CancellationToken cancellationToken = default) =>
        QueryTrackerStatusAsync(DefaultAckTimeout, cancellationToken);

    // ------------------------------------------------------------------
    // Instrument auto-effects
    // ------------------------------------------------------------------

    /// <summary>
    /// Attaches an automatic effect to an instrument (command <c>AC</c>).
    /// The tracker re-arms the effect on every note-on with that instrument,
    /// so e.g. a lead instrument can carry vibrato through a whole song
    /// without any per-row effect data. Type <see cref="SoundBridgeEffect.Off"/>
    /// clears the binding.
    /// </summary>
    public Task<bool?> SetInstrumentEffectAsync(byte instrumentId, SoundBridgeEffect effect, byte speed, byte depth, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SendAudioCommandAsync('C', new[] { instrumentId, (byte)effect, speed, depth }, timeout, cancellationToken);

    /// <inheritdoc cref="SetInstrumentEffectAsync(byte,SoundBridgeEffect,byte,byte,TimeSpan,CancellationToken)"/>
    public Task<bool?> SetInstrumentEffectAsync(byte instrumentId, SoundBridgeEffect effect, byte speed, byte depth, CancellationToken cancellationToken = default) =>
        SetInstrumentEffectAsync(instrumentId, effect, speed, depth, DefaultAckTimeout, cancellationToken);

    // ------------------------------------------------------------------
    // Song upload convenience
    // ------------------------------------------------------------------

    /// <summary>
    /// Compiles <paramref name="song"/> for <paramref name="address"/>,
    /// uploads it in checked chunks and binds it. Follow with
    /// <see cref="PlaySongAsync(byte,CancellationToken)"/>.
    /// </summary>
    public async Task<bool> UploadSongAsync(ushort address, TrackerSong song, CancellationToken cancellationToken = default)
    {
        var compiled = song.Compile(address);
        var uploaded = await StoreMemoryLargeCheckedAsync(address, compiled, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!uploaded) return false;

        var bound = await BindSongAsync(address, cancellationToken).ConfigureAwait(false);
        return bound == true;
    }
}

/// <summary>Tracker playback state reported by <c>A7</c>/<c>AY</c>.</summary>
public enum TrackerPlaybackState : byte
{
    Stopped = 0,
    Playing = 1,
    Paused = 2,
    Remote = 3,
}

/// <summary>Snapshot returned by <see cref="Rift64ProtocolClient.QueryTrackerStatusAsync(CancellationToken)"/>.</summary>
/// <param name="State">Current playback state.</param>
/// <param name="Order">Orderlist position (local mode).</param>
/// <param name="Row">Row within the pattern, or a wrapping streamed-row counter in remote mode.</param>
/// <param name="BufferedRows">Remote rows currently buffered (0-32).</param>
/// <param name="Underruns">Times the remote feed ran dry (clamped at 15).</param>
/// <param name="Overruns">Streamed rows dropped because the ring was full (clamped at 15).</param>
public sealed record TrackerStatus(
    TrackerPlaybackState State,
    byte Order,
    byte Row,
    byte BufferedRows,
    byte Underruns,
    byte Overruns);

/// <summary>
/// One tracker row: a note/instrument pair for each of the 3 SID voices.
/// Matches the client's 6-byte wire/song format. Build rows with
/// <see cref="SetNote(int,string,byte)"/>, <see cref="SetNoteOff"/> and
/// <see cref="SetDrum"/>; a default row is all rests.
/// </summary>
public struct TrackerRow
{
    private byte _n0, _i0, _n1, _i1, _n2, _i2;

    /// <summary>
    /// Note on: <paramref name="note"/> is a name like <c>"C-4"</c> or
    /// <c>"A#3"</c> (octaves 0-7). <paramref name="instrument"/> is a
    /// SoundBridge instrument id 1-16, or 0 to reuse the voice's previous
    /// instrument.
    /// </summary>
    public void SetNote(int voice, string note, byte instrument = 0) =>
        SetNote(voice, SidNote.Index(note), instrument);

    /// <summary>Note on by raw note index (1-95, C-0..B-7).</summary>
    public void SetNote(int voice, byte noteIndex, byte instrument = 0)
    {
        if (noteIndex is < 1 or > 95)
            throw new ArgumentOutOfRangeException(nameof(noteIndex), "Note index must be 1-95.");
        if (instrument > 16)
            throw new ArgumentOutOfRangeException(nameof(instrument), "Instrument must be 0-16 (0 = reuse previous).");
        Set(voice, noteIndex, instrument);
    }

    /// <summary>Release the voice's gate (note off).</summary>
    public void SetNoteOff(int voice) => Set(voice, SidNote.NoteOff, 0);

    /// <summary>Trigger the SFX/drum script in <paramref name="sfxSlot"/> (0-15) on this voice.</summary>
    public void SetDrum(int voice, byte sfxSlot)
    {
        if (sfxSlot > 15)
            throw new ArgumentOutOfRangeException(nameof(sfxSlot), "SFX slot must be 0-15.");
        Set(voice, (byte)(sfxSlot + 1), 0x80);
    }

    private void Set(int voice, byte note, byte inst)
    {
        switch (voice)
        {
            case 0: _n0 = note; _i0 = inst; break;
            case 1: _n1 = note; _i1 = inst; break;
            case 2: _n2 = note; _i2 = inst; break;
            default: throw new ArgumentOutOfRangeException(nameof(voice), "Voice must be 0-2.");
        }
    }

    /// <summary>Writes the row's 6 wire bytes into <paramref name="destination"/>.</summary>
    public readonly void CopyTo(Span<byte> destination)
    {
        destination[0] = _n0;
        destination[1] = _i0;
        destination[2] = _n1;
        destination[3] = _i1;
        destination[4] = _n2;
        destination[5] = _i2;
    }
}
