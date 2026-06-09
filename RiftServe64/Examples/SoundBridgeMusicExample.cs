using RiftServe64.Sdk.Protocol;

/// <summary>
/// Plays predefined multi-voice music through the SoundBridge engine, showing
/// host-sequenced SID polyphony.
/// <para>
/// Each note is a 16-bit PAL SID frequency value written to one of the chip's
/// three voices. The host sequences a short canon and a longer arrangement by
/// timing Note On/Off commands across <see cref="SoundBridgeVoice"/> Voice1-3,
/// after defining instruments (waveform + ADSR) once up front. This
/// demonstrates that musical timing can live on the host while the C64 SID acts
/// purely as the synthesis back-end.
/// </para>
/// </summary>
public sealed class SoundBridgeMusicExample : IRift64MenuExample
{
    public char Key => 'H';
    public string MenuLabel => "SndBridge Music";

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

    // Phase 2.3: best-effort restoration that stops every active voice/effect and
    // resets the SoundBridge so the SID is silent after a fault or cancellation.
    private static async Task RestoreClientStateAsync(Rift64ProtocolClient client)
    {
        try
        {
            await client.SoundBridgeStopAllAsync(CancellationToken.None);
            await client.SoundBridgeResetAsync(CancellationToken.None);
        }
        catch
        {
            // The connection may already be gone during shutdown; ignore.
        }
    }

    private async Task RunCoreAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 12: PREDEFINED MUSIC TRACKS", Rift64Color.LightGreen, cancellationToken);

        // 1. Reset SoundBridge & SID
        await client.WriteAtAsync(0, 2, "1. INITIALIZING SOUNDBRIDGE (AR)...", Rift64Color.White, cancellationToken);
        await client.SoundBridgeResetAsync(cancellationToken);

        // 2. Set Volume to maximum
        await client.WriteAtAsync(0, 3, "2. SETTING VOLUME TO 15 (AV)...", Rift64Color.White, cancellationToken);
        await client.SoundBridgeSetVolumeAsync(15, cancellationToken);

        // 3. Define Instruments
        await client.WriteAtAsync(0, 4, "3. DEFINING INSTRUMENTS (AI)...", Rift64Color.White, cancellationToken);
        
        // Instrument 1: Sawtooth Lead (sawtooth wave $21, medium decay, sustain release)
        await client.SoundBridgeDefineInstrumentAsync(1, pulseWidth: 0x0800, attackDecay: 0x04, sustainRelease: 0x86, control: SidWaveform.Sawtooth | SidWaveform.Gate, cancellationToken);
        
        // Instrument 2: Triangle Chime (triangle wave $11, slow attack, long release)
        await client.SoundBridgeDefineInstrumentAsync(2, pulseWidth: 0x0000, attackDecay: 0x0B, sustainRelease: 0x09, control: SidWaveform.Triangle | SidWaveform.Gate, cancellationToken);

        // Instrument 3: Pulse wave lead (pulse wave $41, pulse width $0400, snappy attack)
        await client.SoundBridgeDefineInstrumentAsync(3, pulseWidth: 0x0400, attackDecay: 0x01, sustainRelease: 0xF8, control: SidWaveform.Pulse | SidWaveform.Gate, cancellationToken);

        // Set to SOUNDBRIDGE_ONLY mode (AM01)
        await client.SoundBridgeSetModeAsync(SoundBridgeAudioMode.SoundBridgeOnly, cancellationToken);

        await client.ClearScreenAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            // UI Header
            await client.WriteAtAsync(0, 0, "RIFT64 PREDEFINED MUSIC PLAYER", Rift64Color.LightGreen, cancellationToken);
            await client.WriteAtAsync(0, 2, "Plays multi-voice classical tracks on SID.", Rift64Color.White, cancellationToken);

            await client.WriteAtAsync(0, 5, "SELECT A PREDEFINED TRACK:", Rift64Color.Cyan, cancellationToken);
            await client.WriteAtAsync(0, 7, "1) Play Pachelbel Canon (3-Voice)", Rift64Color.White, cancellationToken);
            await client.WriteAtAsync(0, 8, "2) Play Dona Nobis Pacem (String Trio)", Rift64Color.White, cancellationToken);
            await client.WriteAtAsync(0, 9, "3) Play Albinoni Adagio in G Minor", Rift64Color.White, cancellationToken);
            await client.WriteAtAsync(0, 11, "B) Stop Current Playback / All Sounds", Rift64Color.White, cancellationToken);
            await client.WriteAtAsync(0, 13, "Space) Return to Menu", Rift64Color.White, cancellationToken);

            var key = await client.ReadKeyAsync(TimeSpan.FromMinutes(5), cancellationToken);
            if (key is null) continue;

            if (key is ' ')
            {
                break;
            }

            // 1: Pachelbel Canon
            if (key is '1')
            {
                await PlayShortTuneAsync(client, cancellationToken);
            }
            // 2: Dona Nobis Pacem
            else if (key is '2')
            {
                await PlayDonaNobisPacemAsync(client, cancellationToken);
            }
            // 3: Albinoni Adagio
            else if (key is '3')
            {
                await PlayAlbinoniAsync(client, cancellationToken);
            }
            // B: Stop/silence all sounds (AZ)
            else if (key is 'b' or 'B')
            {
                await client.WriteAtAsync(0, 22, "SILENCING ALL AUDIO (AZ)...                   ", Rift64Color.White, cancellationToken);
                await client.SoundBridgeStopAllAsync(cancellationToken);
            }
        }

        // Clean up SID before exiting
        await client.SoundBridgeResetAsync(cancellationToken);
        await client.ClearScreenAsync(cancellationToken);
    }

    private async Task PlayShortTuneAsync(Rift64ProtocolClient client, CancellationToken cancellationToken)
    {
        var bars = new ClassicalBar[] {
            // Bar 1: C Major
            new ClassicalBar(0x1167, 0x15E8, new ushort[] { 0x2BD0, 0x270E, 0x22CD, 0x211D }),
            // Bar 2: G Major
            new ClassicalBar(0x0E9D, 0x1387, new ushort[] { 0x211D, 0x1D43, 0x1A09, 0x172E }),
            // Bar 3: A Minor
            new ClassicalBar(0x0D02, 0x1167, new ushort[] { 0x1D43, 0x211D, 0x22CD, 0x2BD0 }),
            // Bar 4: E Minor
            new ClassicalBar(0x0B52, 0x0E9D, new ushort[] { 0x1A09, 0x1D43, 0x211D, 0x270E }),
            // Bar 5: F Major
            new ClassicalBar(0x0A09, 0x0D02, new ushort[] { 0x172E, 0x15E8, 0x172E, 0x1D43 }),
            // Bar 6: C Major
            new ClassicalBar(0x08F6, 0x0B52, new ushort[] { 0x15E8, 0x1387, 0x15E8, 0x1A09 }),
            // Bar 7: F Major
            new ClassicalBar(0x0A09, 0x0D02, new ushort[] { 0x172E, 0x1A09, 0x1D43, 0x172E }),
            // Bar 8: G Major
            new ClassicalBar(0x0B52, 0x0E9D, new ushort[] { 0x1A09, 0x1D43, 0x211D, 0x1A09 }),

            // Bar 9: C Major (Arpeggiated Melody)
            new ClassicalBar(0x1167, 0x15E8, new ushort[] { 0x1A09, 0x22CD, 0x2BD0, 0x3412 }),
            // Bar 10: G Major
            new ClassicalBar(0x0E9D, 0x1387, new ushort[] { 0x3412, 0x2F5B, 0x2BD0, 0x270E }),
            // Bar 11: A Minor
            new ClassicalBar(0x0D02, 0x1167, new ushort[] { 0x2BD0, 0x1D43, 0x22CD, 0x2BD0 }),
            // Bar 12: E Minor
            new ClassicalBar(0x0B52, 0x0E9D, new ushort[] { 0x2BD0, 0x270E, 0x22CD, 0x211D }),
            // Bar 13: F Major
            new ClassicalBar(0x0A09, 0x0D02, new ushort[] { 0x22CD, 0x1D43, 0x172E, 0x1D43 }),
            // Bar 14: C Major
            new ClassicalBar(0x08F6, 0x0B52, new ushort[] { 0x22CD, 0x1A09, 0x15E8, 0x1A09 }),
            // Bar 15: F Major
            new ClassicalBar(0x0A09, 0x0D02, new ushort[] { 0x1D43, 0x22CD, 0x2F5B, 0x3B40 }),
            // Bar 16: G Major
            new ClassicalBar(0x0B52, 0x0E9D, new ushort[] { 0x3412, 0x270E, 0x211D, 0x1A09 })
        };

        await client.WriteAtAsync(0, 22, "PLAYING 16-BAR CLASSICAL PACHELBEL CANON...  ", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(0, 23, "Press any key to stop playback.              ", Rift64Color.Yellow, cancellationToken);

        for (int i = 0; i < bars.Length; i++)
        {
            var bar = bars[i];
            
            // 1. Play sustained 2-note chord (V1 and V2) using Instrument 2 (Bell Chime)
            await client.SoundBridgeNoteOnAsync(SoundBridgeVoice.Voice1, bar.Chord0, 2, cancellationToken);
            await client.SoundBridgeNoteOnAsync(SoundBridgeVoice.Voice2, bar.Chord1, 2, cancellationToken);

            // 2. Play 4 melody notes in sequence on Voice 3 (Instrument 3, Pulse)
            for (int m = 0; m < 4; m++)
            {
                if (await StopRequestedAsync(client, cancellationToken)) return;

                ushort melFreq = bar.Melody[m];
                await client.SoundBridgeNoteOnAsync(SoundBridgeVoice.Voice3, melFreq, 3, cancellationToken);

                // Note duration (300ms)
                await Task.Delay(300, cancellationToken);

                // Snappy release before next melody note
                await client.SoundBridgeNoteOffAsync(SoundBridgeVoice.Voice3, cancellationToken);
                await Task.Delay(50, cancellationToken);
            }

            // 3. Release sustained chord notes at the end of the bar
            await client.SoundBridgeNoteOffAsync(SoundBridgeVoice.Voice1, cancellationToken);
            await client.SoundBridgeNoteOffAsync(SoundBridgeVoice.Voice2, cancellationToken);
            
            // Short gap between measures
            await Task.Delay(50, cancellationToken);
        }

        // Final Resolution: Sustained C-Major Chord
        if (await StopRequestedAsync(client, cancellationToken)) return;
        await client.SoundBridgeNoteOnAsync(SoundBridgeVoice.Voice1, 0x1167, 2, cancellationToken); // C-4
        await client.SoundBridgeNoteOnAsync(SoundBridgeVoice.Voice2, 0x15E8, 2, cancellationToken); // E-4
        await client.SoundBridgeNoteOnAsync(SoundBridgeVoice.Voice3, 0x22CD, 3, cancellationToken); // C-5
        await Task.Delay(1500, cancellationToken);
        await client.SoundBridgeNoteOffAsync(SoundBridgeVoice.Voice1, cancellationToken);
        await client.SoundBridgeNoteOffAsync(SoundBridgeVoice.Voice2, cancellationToken);
        await client.SoundBridgeNoteOffAsync(SoundBridgeVoice.Voice3, cancellationToken);

        await client.WriteAtAsync(0, 22, "CLASSICAL TUNE FINISHED.                      ", Rift64Color.Cyan, cancellationToken);
        await client.WriteAtAsync(0, 23, "                                             ", Rift64Color.Yellow, cancellationToken);
    }

    private async Task PlayDonaNobisPacemAsync(Rift64ProtocolClient client, CancellationToken cancellationToken)
    {
        await client.WriteAtAsync(0, 22, "PLAYING CLASSICAL TRIO: DONA NOBIS PACEM...   ", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(0, 23, "Press any key to stop playback.              ", Rift64Color.Yellow, cancellationToken);

        var events = DonaNobisPacemData.Events;
        int lastTimeMs = 0;

        // Track active frequencies on each voice to prevent overlapping note-off cutoffs
        ushort[] activeFreqs = new ushort[3];

        for (int i = 0; i < events.Count; i++)
        {
            var ev = events[i];

            // Wait for delta time
            int deltaMs = ev.TimeMs - lastTimeMs;
            if (deltaMs > 0)
            {
                if (await StopRequestedAsync(client, cancellationToken)) return;
                await Task.Delay(deltaMs, cancellationToken);
                lastTimeMs = ev.TimeMs;
            }

            if (ev.IsOn)
            {
                // Note On: Play note using Instrument 1 (Sawtooth String)
                activeFreqs[ev.Voice] = ev.Freq;
                await client.SoundBridgeNoteOnAsync(ev.Voice, ev.Freq, instrumentId: 1, cancellationToken);
            }
            else
            {
                // Note Off: Only clear if this note matches the currently active frequency on this voice
                if (activeFreqs[ev.Voice] == ev.Freq)
                {
                    await client.SoundBridgeNoteOffAsync(ev.Voice, cancellationToken);
                    activeFreqs[ev.Voice] = 0;
                }
            }
        }

        // Silence everything at the end
        await client.SoundBridgeStopAllAsync(cancellationToken);
        await client.WriteAtAsync(0, 22, "TRIO FINISHED.                                ", Rift64Color.Cyan, cancellationToken);
        await client.WriteAtAsync(0, 23, "                                             ", Rift64Color.Yellow, cancellationToken);
    }

    private async Task PlayAlbinoniAsync(Rift64ProtocolClient client, CancellationToken cancellationToken)
    {
        await client.WriteAtAsync(0, 22, "PLAYING CLASSICAL ADAGIO: ALBINONI IN G MIN... ", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(0, 23, "Press any key to stop playback.              ", Rift64Color.Yellow, cancellationToken);

        var events = AlbinoniData.Events;
        int lastTimeMs = 0;

        ushort[] activeFreqs = new ushort[3];

        for (int i = 0; i < events.Count; i++)
        {
            var ev = events[i];

            int deltaMs = ev.TimeMs - lastTimeMs;
            if (deltaMs > 20)
            {
                if (await StopRequestedAsync(client, cancellationToken)) return;
                await Task.Delay(deltaMs, cancellationToken);
                lastTimeMs = ev.TimeMs;
            }

            if (ev.IsOn)
            {
                // Note On: Play note using Instrument 1 (Sawtooth String)
                activeFreqs[ev.Voice] = ev.Freq;
                await client.SoundBridgeNoteOnAsync(ev.Voice, ev.Freq, instrumentId: 1, cancellationToken);
            }
            else
            {
                // Note Off: Only clear if this note matches the currently active frequency on this voice
                if (activeFreqs[ev.Voice] == ev.Freq)
                {
                    await client.SoundBridgeNoteOffAsync(ev.Voice, cancellationToken);
                    activeFreqs[ev.Voice] = 0;
                }
            }
        }

        // Silence everything at the end
        await client.SoundBridgeStopAllAsync(cancellationToken);
        await client.WriteAtAsync(0, 22, "ADAGIO FINISHED.                              ", Rift64Color.Cyan, cancellationToken);
        await client.WriteAtAsync(0, 23, "                                             ", Rift64Color.Yellow, cancellationToken);
    }

    // Non-blocking check for a stop request during playback. Returns true
    // (and silences all voices) when the app is cancelling or the user has
    // pressed any key to stop the current track. Shared by every track so
    // they all stop the same way.
    private async Task<bool> StopRequestedAsync(Rift64ProtocolClient client, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            await client.SoundBridgeStopAllAsync(cancellationToken);
            return true;
        }

        var abortKey = await client.ReadKeyAsync(TimeSpan.Zero, cancellationToken);
        if (abortKey is null)
        {
            return false;
        }

        await client.SoundBridgeStopAllAsync(cancellationToken);
        await client.WriteAtAsync(0, 22, "PLAYBACK ABORTED BY USER.                      ", Rift64Color.Yellow, cancellationToken);
        await client.WriteAtAsync(0, 23, "                                             ", Rift64Color.Yellow, cancellationToken);
        return true;
    }

    private sealed class ClassicalBar
    {
        public ushort Chord0 { get; }
        public ushort Chord1 { get; }
        public ushort[] Melody { get; }

        public ClassicalBar(ushort c0, ushort c1, ushort[] melody)
        {
            Chord0 = c0;
            Chord1 = c1;
            Melody = melody;
        }
    }
}