using RiftServe64.Sdk.Protocol;

/// <summary>
/// Demonstrates the remote tracker: instead of uploading a song, the host
/// streams pattern rows live (<c>AU</c>) into the client's 32-row ring
/// buffer, where the same row decoder consumes them at row rate. The host
/// paces itself with the <c>AY</c> status query, keeping ~24 rows buffered.
/// Press D to deliberately stall the feed: sustaining voices keep ringing
/// (the underrun-hold policy) and the underrun counter ticks up, visible in
/// the status line once streaming resumes.
/// </summary>
public sealed class RemoteTrackerExample : IRift64MenuExample
{
    public char Key => 'K';
    public string MenuLabel => "Remote Tracker";

    private const int TargetBufferedRows = 24;

    public async Task RunAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        try
        {
            await RunCoreAsync(client, cancellationToken);
        }
        finally
        {
            try
            {
                await client.SetTrackerRemoteModeAsync(false, CancellationToken.None);
            }
            catch
            {
                // The connection may already be gone during shutdown; ignore.
            }
        }
    }

    private async Task RunCoreAsync(Rift64ProtocolClient client, CancellationToken cancellationToken)
    {
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE: REMOTE TRACKER (AT/AU/AY)", Rift64Color.LightGreen, cancellationToken);

        await client.WriteAtAsync(0, 2, "1. SETTING UP INSTRUMENTS + DRUMS...", Rift64Color.White, cancellationToken);
        await client.SoundBridgeResetAsync(cancellationToken);
        await client.SetAudioVolumeAsync(15, cancellationToken);
        await client.SoundBridgeDefineInstrumentAsync(1, pulseWidth: 0x0000, attackDecay: 0x09, sustainRelease: 0x55, control: SidWaveform.Sawtooth | SidWaveform.Gate, cancellationToken);
        await client.SoundBridgeDefineInstrumentAsync(2, pulseWidth: 0x0600, attackDecay: 0x18, sustainRelease: 0x97, control: SidWaveform.Triangle | SidWaveform.Gate, cancellationToken);
        await client.UploadDrumKitAsync(0xC0, SidDrumKit.StandardKit(), cancellationToken);

        await client.WriteAtAsync(0, 3, "2. SETTING SPEED + ENTERING REMOTE MODE (AT)...", Rift64Color.White, cancellationToken);
        await client.SetSongSpeedAsync(6, cancellationToken);
        var entered = await client.SetTrackerRemoteModeAsync(true, cancellationToken);
        if (entered != true)
        {
            await client.WriteAtAsync(0, 5, "CLIENT REFUSED REMOTE MODE!", Rift64Color.Red, cancellationToken);
            await client.PauseForKeyAsync(cancellationToken: cancellationToken);
            return;
        }

        await client.WriteAtAsync(0, 5, "STREAMING ROWS - WATCH THE BUFFER GAUGE.", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(0, 7, "D) STALL FEED 3S (UNDERRUN DEMO)  Q) QUIT", Rift64Color.Cyan, cancellationToken);

        var rows = BuildRows();
        var next = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var status = await client.QueryTrackerStatusAsync(cancellationToken);
            if (status is not null)
            {
                var gauge = new string('*', status.BufferedRows).PadRight(32, '.');
                await client.WriteAtAsync(0, 9, $"BUFFER: {gauge}", Rift64Color.White, cancellationToken);
                await client.WriteAtAsync(0, 11, $"ROWS PLAYED: {status.Row:D3}  UNDERRUNS: {status.Underruns:D2}  OVERRUNS: {status.Overruns:D2}", Rift64Color.White, cancellationToken);

                var deficit = TargetBufferedRows - status.BufferedRows;
                if (deficit > 0)
                {
                    var batch = new TrackerRow[deficit];
                    for (var i = 0; i < deficit; i++)
                    {
                        batch[i] = rows[next];
                        next = (next + 1) % rows.Length;
                    }
                    await client.StreamTrackerRowsAsync(batch, cancellationToken);
                }
            }

            var key = await client.ReadKeyAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
            if (key is 'q' or 'Q') break;

            if (key is 'd' or 'D')
            {
                await client.WriteAtAsync(0, 13, "FEED STALLED... NOTES HOLD, NOTHING RETRIGGERS", Rift64Color.Yellow, cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                await client.WriteAtAsync(0, 13, "FEED RESUMED.                                 ", Rift64Color.LightGreen, cancellationToken);
            }
        }

        await client.SetTrackerRemoteModeAsync(false, cancellationToken);
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "REMOTE TRACKER COMPLETE.", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(0, 2, "PRESS ANY KEY TO RETURN TO MENU.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);
    }

    // The streamed material is ordinary tracker rows — the same structures a
    // local song is built from, just fed over the wire instead of uploaded.
    private static TrackerRow[] BuildRows()
    {
        var song = new TrackerSong(rowsPerPattern: 32);
        var p = song.AddPattern();

        for (var bar = 0; bar < 32; bar += 8)
        {
            p.SetDrum(bar + 0, 2, 0);
            p.SetDrum(bar + 2, 2, 2);
            p.SetDrum(bar + 4, 2, 1);
            p.SetDrum(bar + 6, 2, 2);
        }

        p.SetNote(0, 0, "A-2", 1).SetNote(8, 0, "F-2", 1).SetNote(16, 0, "C-2", 1).SetNote(24, 0, "G-2", 1);
        p.SetNote(0, 1, "A-4", 2).SetNote(4, 1, "C-5", 2).SetNote(8, 1, "F-4", 2).SetNote(12, 1, "A-4", 2)
         .SetNote(16, 1, "E-4", 2).SetNote(20, 1, "G-4", 2).SetNote(24, 1, "B-4", 2).SetNote(28, 1, "D-5", 2);

        return p.ToRows();
    }
}
