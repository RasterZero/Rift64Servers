using RiftServe64.Sdk.Protocol;

/// <summary>
/// Demonstrates the client-side tracker: a song with bass, lead and drums is
/// composed in C# (<see cref="TrackerSong"/>), compiled to the client binary
/// format, uploaded once into the $4000+ zone and played entirely on the C64
/// from the jiffy IRQ — perfectly timed and immune to network jitter, with
/// zero bandwidth while playing. Drums are SFX bytecode scripts
/// (<see cref="SidDrumKit"/>) triggered from tracker rows on voice 3, and
/// the lead instrument carries an automatic vibrato bound with <c>AC</c>.
/// The host keeps a live position display going via the <c>AY</c> status
/// query and drives the transport (play/pause/resume/stop/jump).
/// </summary>
public sealed class AudioPlayerExample : IRift64MenuExample
{
    public char Key => 'A';
    public string MenuLabel => "Tracker Music";

    public async Task RunAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        try
        {
            await RunCoreAsync(client, initialIdentity, cancellationToken);
        }
        finally
        {
            await RestoreClientStateAsync(client);
        }
    }

    private static async Task RestoreClientStateAsync(Rift64ProtocolClient client)
    {
        try
        {
            await client.StopSongAsync(CancellationToken.None);
        }
        catch
        {
            // The connection may already be gone during shutdown; ignore.
        }
    }

    private async Task RunCoreAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 10: TRACKER MUSIC (A)", Rift64Color.LightGreen, cancellationToken);

        // 1. Clean slate
        await client.WriteAtAsync(0, 2, "1. RESETTING SOUNDBRIDGE (AR)...", Rift64Color.White, cancellationToken);
        await client.SoundBridgeResetAsync(cancellationToken);
        await client.SetAudioVolumeAsync(15, cancellationToken);

        // 2. Instruments: 1 = saw bass, 2 = pulse lead with auto-vibrato
        await client.WriteAtAsync(0, 3, "2. DEFINING INSTRUMENTS (AI/AC)...", Rift64Color.White, cancellationToken);
        await client.SoundBridgeDefineInstrumentAsync(1, pulseWidth: 0x0000, attackDecay: 0x09, sustainRelease: 0x55, control: SidWaveform.Sawtooth | SidWaveform.Gate, cancellationToken);
        await client.SoundBridgeDefineInstrumentAsync(2, pulseWidth: 0x0600, attackDecay: 0x18, sustainRelease: 0x97, control: SidWaveform.Pulse | SidWaveform.Gate, cancellationToken);
        await client.SetInstrumentEffectAsync(instrumentId: 2, SoundBridgeEffect.Vibrato, speed: 0x28, depth: 0x10, cancellationToken);

        // 3. Drum kit into the SFX bank: slot 0 kick, 1 snare, 2 closed hat
        await client.WriteAtAsync(0, 4, "3. UPLOADING DRUM KIT ($C000)...", Rift64Color.White, cancellationToken);
        var kitOk = await client.UploadDrumKitAsync(0xC0, SidDrumKit.StandardKit(), cancellationToken);

        // 4. Compile + upload + bind the song
        var song = BuildSong();
        await client.WriteAtAsync(0, 5, $"4. UPLOADING SONG TO ${Rift64ProtocolClient.TrackerSongAddress:X4} (A5)...", Rift64Color.White, cancellationToken);
        var songOk = await client.UploadSongAsync(Rift64ProtocolClient.TrackerSongAddress, song, cancellationToken);

        if (!kitOk || !songOk)
        {
            await client.WriteAtAsync(0, 7, "UPLOAD FAILED or TIMED OUT!", Rift64Color.Red, cancellationToken);
            await client.WriteAtAsync(0, 9, "PRESS ANY KEY TO RETURN.", Rift64Color.Yellow, cancellationToken);
            await client.PauseForKeyAsync(cancellationToken: cancellationToken);
            return;
        }

        // 5. Play from order 0 — the client sequences on its own from here
        await client.WriteAtAsync(0, 6, "5. STARTING PLAYBACK (A1)...", Rift64Color.White, cancellationToken);
        await client.PlaySongAsync(0, cancellationToken);

        await client.WriteAtAsync(0, 8, "PLAYING - THE HOST IS NOW IDLE.", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(0, 10, "P) PAUSE   R) RESUME   S) STOP", Rift64Color.Cyan, cancellationToken);
        await client.WriteAtAsync(0, 11, "J) JUMP TO ORDER 1     Q) QUIT", Rift64Color.Cyan, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            // Live position from the AY status query while waiting for keys
            var status = await client.QueryTrackerStatusAsync(cancellationToken);
            if (status is not null)
            {
                await client.WriteAtAsync(0, 13, $"STATE: {status.State,-8} ORDER: {status.Order:D2} ROW: {status.Row:D2}   ", Rift64Color.White, cancellationToken);
            }

            var key = await client.ReadKeyAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
            if (key is null) continue;

            if (key is 'q' or 'Q') break;

            if (key is 'p' or 'P')
            {
                await client.PauseSongAsync(cancellationToken);
            }
            else if (key is 'r' or 'R')
            {
                await client.ResumeSongAsync(cancellationToken);
            }
            else if (key is 's' or 'S')
            {
                await client.StopSongAsync(cancellationToken);
            }
            else if (key is 'j' or 'J')
            {
                await client.JumpToOrderAsync(1, cancellationToken);
            }
        }

        await client.StopSongAsync(cancellationToken);
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 10: COMPLETE", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(0, 2, "TRACKER PLAYBACK COMPLETED.", Rift64Color.White, cancellationToken);
        await client.WriteAtAsync(0, 4, "PRESS ANY KEY TO RETURN TO MENU.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);
    }

    // Two 32-row patterns: saw bass on V1, vibrato lead on V2 and a
    // kick/snare/hat groove on V3 (drums = SFX slots 0-2).
    private static TrackerSong BuildSong()
    {
        var song = new TrackerSong(rowsPerPattern: 32) { Speed = 6, LoopOrder = 0 };

        var verse = song.AddPattern();
        AddGroove(verse);
        AddBass(verse, "C-2", "G-2", "A-2", "F-2");
        verse.SetNote(0, 1, "E-4", 2).SetNote(6, 1, "G-4", 2).SetNote(12, 1, "C-5", 2)
             .SetNote(16, 1, "B-4", 2).SetNote(22, 1, "G-4", 2).SetNote(28, 1, "A-4", 2);

        var chorus = song.AddPattern();
        AddGroove(chorus);
        AddBass(chorus, "A-2", "F-2", "C-2", "G-2");
        chorus.SetNote(0, 1, "C-5", 2).SetNote(4, 1, "A-4", 2).SetNote(8, 1, "F-4", 2)
              .SetNote(12, 1, "A-4", 2).SetNote(16, 1, "G-4", 2).SetNote(24, 1, "E-4", 2)
              .SetNoteOff(30, 1);

        song.Order.AddRange([0, 0, 1, 1]);
        return song;
    }

    private static void AddGroove(TrackerPattern p)
    {
        for (var bar = 0; bar < 32; bar += 8)
        {
            p.SetDrum(bar + 0, 2, 0);      // kick
            p.SetDrum(bar + 2, 2, 2);      // closed hat
            p.SetDrum(bar + 4, 2, 1);      // snare
            p.SetDrum(bar + 6, 2, 2);      // closed hat
        }
    }

    private static void AddBass(TrackerPattern p, params string[] notesPerHalfBar)
    {
        for (var i = 0; i < notesPerHalfBar.Length; i++)
        {
            p.SetNote(i * 8, 0, notesPerHalfBar[i], 1);
            p.SetNote(i * 8 + 4, 0, notesPerHalfBar[i], 1);
        }
    }
}
