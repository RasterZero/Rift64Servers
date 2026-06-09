using RiftServe64.Sdk.Protocol;

/// <summary>
/// "NEON DRIFT" — an original, upbeat three-voice piece showcasing the
/// SoundBridge local note-effect engine (AE command): a vibrato sawtooth
/// lead, a PWM octave bass, and arpeggiated chords + portamento fills on the
/// accent voice, all running in the C64 jiffy IRQ.
///
/// Engine notes:
///  * Effects persist across Note Ons, so each effect is armed once with
///    SetEffect and keeps running as new notes are triggered.
///  * Arpeggio (FxArp) turns a single voice into a chord by cycling
///    root -> 3rd -> 5th; major/minor is selected per chord.
///  * Slide (FxSlide) is portamento: each new note glides from the previous
///    note on that voice.
///  * PWM only colours a PULSE-waveform voice ($40 control bit).
/// </summary>
public sealed class SoundBridgeFxShowcaseExample : IRift64MenuExample
{
    public char Key => 'I';
    public string MenuLabel => "SndBridge FX";

    // Effect type codes (AE) — from the SDK enum
    private const SoundBridgeEffect FxOff = SoundBridgeEffect.Off;
    private const SoundBridgeEffect FxVibrato = SoundBridgeEffect.Vibrato;
    private const SoundBridgeEffect FxSlide = SoundBridgeEffect.Slide;
    private const SoundBridgeEffect FxPwm = SoundBridgeEffect.Pwm;
    private const SoundBridgeEffect FxArp = SoundBridgeEffect.Arpeggio;

    // Voice assignment for the piece — from the SDK enum
    private const SoundBridgeVoice VLead = SoundBridgeVoice.Voice1;   // saw lead + vibrato
    private const SoundBridgeVoice VBass = SoundBridgeVoice.Voice2;   // pulse bass + PWM
    private const SoundBridgeVoice VAccent = SoundBridgeVoice.Voice3; // pulse pluck + slide

    // --- Note table (PAL SID register values) -----------------------------
    // reg = round(freq_hz * 16777216 / 985248)
    private const ushort F2  = 0x05CF; // F2  87.31
    private const ushort G2  = 0x0685; // G2  98.00
    private const ushort A2  = 0x0751; // A2 110.00
    private const ushort C3  = 0x08B3; // C3 130.81
    private const ushort E3  = 0x0AF7; // E3 164.81
    private const ushort F3  = 0x0B9D; // F3 174.61
    private const ushort G3  = 0x0D0A; // G3 196.00
    private const ushort A3  = 0x0EA2; // A3 220.00
    private const ushort C4  = 0x1167; // C4 261.63
    private const ushort D4  = 0x1389; // D4 293.66
    private const ushort E4  = 0x15ED; // E4 329.63
    private const ushort F4  = 0x173B; // F4 349.23
    private const ushort G4  = 0x1A13; // G4 392.00
    private const ushort A4  = 0x1D44; // A4 440.00
    private const ushort B4  = 0x20D9; // B4 493.88
    private const ushort C5  = 0x22CE; // C5 523.25
    private const ushort D5  = 0x2711; // D5 587.33
    private const ushort E5  = 0x2BDA; // E5 659.25
    private const ushort A5  = 0x3A88; // A5 880.00

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
        await client.WriteAtAsync(0, 0, "EXAMPLE 13: NEON DRIFT (FX SHOWCASE)", Rift64Color.LightGreen, cancellationToken);

        await client.WriteAtAsync(0, 2, "1. INITIALIZING SOUNDBRIDGE (AR)...", Rift64Color.White, cancellationToken);
        await client.SoundBridgeResetAsync(cancellationToken);

        await client.WriteAtAsync(0, 3, "2. SETTING VOLUME TO 15 (AV)...", Rift64Color.White, cancellationToken);
        await client.SoundBridgeSetVolumeAsync(15, cancellationToken);

        await client.WriteAtAsync(0, 4, "3. DEFINING INSTRUMENTS (AI)...", Rift64Color.White, cancellationToken);
        // Lead: sawtooth, snappy attack, full sustain — vibrato carrier.
        await client.SoundBridgeDefineInstrumentAsync(1, pulseWidth: 0x0800, attackDecay: 0x09, sustainRelease: 0xA8, control: SidWaveform.Sawtooth | SidWaveform.Gate, cancellationToken);
        // Bass: pulse, centred pulse width — PWM carrier.
        await client.SoundBridgeDefineInstrumentAsync(2, pulseWidth: 0x0800, attackDecay: 0x00, sustainRelease: 0xC7, control: SidWaveform.Pulse | SidWaveform.Gate, cancellationToken);
        // Accent: pulse, thin pulse, plucky — slide carrier.
        await client.SoundBridgeDefineInstrumentAsync(3, pulseWidth: 0x0300, attackDecay: 0x00, sustainRelease: 0x97, control: SidWaveform.Pulse | SidWaveform.Gate, cancellationToken);

        // SoundBridge controls all three voices.
        await client.SoundBridgeSetModeAsync(SoundBridgeAudioMode.SoundBridgeOnly, cancellationToken);

        await client.ClearScreenAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            await client.WriteAtAsync(0, 0, "RIFT64 - NEON DRIFT (ORIGINAL)", Rift64Color.LightGreen, cancellationToken);
            await client.WriteAtAsync(0, 2, "An original tune showcasing the AE effect engine:", Rift64Color.White, cancellationToken);
            await client.WriteAtAsync(0, 4, "V1 LEAD  - sawtooth melody + VIBRATO", Rift64Color.Cyan, cancellationToken);
            await client.WriteAtAsync(0, 5, "V2 BASS  - pulse octave bass + PWM", Rift64Color.Cyan, cancellationToken);
            await client.WriteAtAsync(0, 6, "V3 CHORD - pulse ARPEGGIO + SLIDE fills", Rift64Color.Cyan, cancellationToken);

            await client.WriteAtAsync(0, 9, "1) Play Neon Drift (full piece)", Rift64Color.White, cancellationToken);
            await client.WriteAtAsync(0, 10, "2) Vibrato solo (V1)", Rift64Color.White, cancellationToken);
            await client.WriteAtAsync(0, 11, "3) PWM solo (V2)", Rift64Color.White, cancellationToken);
            await client.WriteAtAsync(0, 12, "4) Slide solo (V3)", Rift64Color.White, cancellationToken);
            await client.WriteAtAsync(0, 13, "5) Arpeggio chords (V3)", Rift64Color.White, cancellationToken);
            await client.WriteAtAsync(0, 15, "B) Stop All Sounds", Rift64Color.White, cancellationToken);
            await client.WriteAtAsync(0, 17, "Space) Return to Menu", Rift64Color.White, cancellationToken);
            await client.WriteAtAsync(0, 19, "(Any key stops playback in progress)", Rift64Color.MediumGray, cancellationToken);

            var key = await client.ReadKeyAsync(TimeSpan.FromMinutes(5), cancellationToken);
            if (key is null) continue;

            if (key is ' ') break;

            if (key is '1')
                await PlayNeonDriftAsync(client, cancellationToken);
            else if (key is '2')
                await PlayVibratoSoloAsync(client, cancellationToken);
            else if (key is '3')
                await PlayPwmSoloAsync(client, cancellationToken);
            else if (key is '4')
                await PlaySlideSoloAsync(client, cancellationToken);
            else if (key is '5')
                await PlayArpSoloAsync(client, cancellationToken);
            else if (key is 'b' or 'B')
            {
                await client.WriteAtAsync(0, 22, "SILENCING ALL AUDIO (AZ)...                   ", Rift64Color.White, cancellationToken);
                await client.SoundBridgeStopAllAsync(cancellationToken);
            }
        }

        await client.SoundBridgeResetAsync(cancellationToken);
        await client.ClearScreenAsync(cancellationToken);
    }

    // =====================================================================
    // Full piece: upbeat C-G-Am-F (I-V-vi-IV) groove. Vibrato lead melody,
    // PWM octave bass, arpeggiated chords, and portamento fills.
    // =====================================================================
    private async Task PlayNeonDriftAsync(Rift64ProtocolClient client, CancellationToken cancellationToken)
    {
        await client.WriteAtAsync(0, 22, "PLAYING 'NEON DRIFT' - UPBEAT 3-VOICE SHOWCASE ", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(0, 23, "Press any key to stop.                        ", Rift64Color.Yellow, cancellationToken);

        const int Step = 140;   // eighth-note duration (ms) — brisk tempo

        // Progression: C - G - Am - F. Each entry drives all three voices:
        //   Root  = arp chord root on V3 (3rd/5th derived by the engine)
        //   Minor = chord quality for the arpeggio
        //   Bass / BassOct = octave-bounce pulse bass on V2
        var prog = new (ushort Root, bool Minor, ushort Bass, ushort BassOct)[]
        {
            (C4, false, C3, C4),
            (G4, false, G2, G3),
            (A3, true,  A2, A3),
            (F4, false, F2, F3),
        };

        // Verse melody — one 8-step (eighth-note) line per chord; 0 = rest.
        var verse = new ushort[][]
        {
            new ushort[] { E4, G4, C5, 0,  G4, E4, 0,  G4 },
            new ushort[] { D4, G4, B4, 0,  G4, D4, 0,  G4 },
            new ushort[] { E4, A4, C5, 0,  A4, E4, 0,  A4 },
            new ushort[] { F4, A4, C5, 0,  A4, F4, 0,  A4 },
        };

        // Chorus melody — higher and busier for a lift in energy.
        var chorus = new ushort[][]
        {
            new ushort[] { G4, C5, E5, C5, G4, C5, E5, 0 },
            new ushort[] { G4, B4, D5, B4, G4, D5, B4, 0 },
            new ushort[] { A4, C5, E5, C5, A4, E5, C5, 0 },
            new ushort[] { A4, C5, F4, C5, A4, C5, F4, 0 },
        };

        // Arm the persistent voice effects once — they keep running as new
        // notes are triggered (no need to re-send after every Note On).
        await client.SoundBridgeSetEffectAsync(VBass, FxPwm, speed: 0x16, depth: 0x58, cancellationToken);
        await client.SoundBridgeSetEffectAsync(VLead, FxVibrato, speed: 0x28, depth: 0x12, cancellationToken);

        // --- Intro: 2 bars of arp chords + bass only, building the groove.
        if (await PlayBarAsync(client, cancellationToken, prog[0], null, Step, arpHold: 4)) return;
        if (await PlayBarAsync(client, cancellationToken, prog[2], null, Step, arpHold: 4)) return;

        // --- Verse: full progression with the lead melody on top.
        for (int b = 0; b < prog.Length; b++)
            if (await PlayBarAsync(client, cancellationToken, prog[b], verse[b], Step, arpHold: 4)) return;

        // --- Chorus: same chords, busier high melody, tighter arp for drive.
        for (int b = 0; b < prog.Length; b++)
            if (await PlayBarAsync(client, cancellationToken, prog[b], chorus[b], Step, arpHold: 2)) return;

        // --- Slide fill: drop the arp and sweep V3 up with portamento, while
        //     the bass keeps the pulse going underneath.
        if (!cancellationToken.IsCancellationRequested)
        {
            await client.SoundBridgeSetEffectAsync(VAccent, FxSlide, speed: 0xD0, depth: 0x0C, cancellationToken);
            await client.SoundBridgeNoteOnAsync(VAccent, A5, instrumentId: 3, cancellationToken); // glide up from last chord
            for (int step = 0; step < 6; step++)
            {
                if (await AbortedAsync(client, cancellationToken)) return;
                await client.SoundBridgeNoteOnAsync(VBass, (step & 1) == 0 ? A2 : A3, instrumentId: 2, cancellationToken);
                await Task.Delay(Step, cancellationToken);
            }
        }

        // --- Outro: bright C-major stab with all three voices (PWM bass,
        //     vibrato lead, arpeggiated chord), then a portamento fall.
        if (!cancellationToken.IsCancellationRequested)
        {
            await client.SoundBridgeSetEffectAsync(VBass, FxPwm, speed: 0x12, depth: 0x62, cancellationToken);
            await client.SoundBridgeNoteOnAsync(VBass, C3, instrumentId: 2, cancellationToken);

            await client.SoundBridgeSetEffectAsync(VLead, FxVibrato, speed: 0x20, depth: 0x1E, cancellationToken);
            await client.SoundBridgeNoteOnAsync(VLead, C5, instrumentId: 1, cancellationToken);

            // Switch V3 back to arp BEFORE the Note On so it stabs (not glides).
            await client.SoundBridgeSetArpeggioAsync(VAccent, holdFrames: 2, minor: false, cancellationToken);
            await client.SoundBridgeNoteOnAsync(VAccent, C4, instrumentId: 3, cancellationToken);

            await Task.Delay(1500, cancellationToken);

            // Portamento fall on the accent voice to close.
            await client.SoundBridgeSetEffectAsync(VAccent, FxSlide, speed: 0xB0, depth: 0x12, cancellationToken);
            await client.SoundBridgeNoteOnAsync(VAccent, C3, instrumentId: 3, cancellationToken); // glide down
            await Task.Delay(900, cancellationToken);

            await client.SoundBridgeNoteOffAsync(VLead, cancellationToken);
            await client.SoundBridgeNoteOffAsync(VBass, cancellationToken);
            await client.SoundBridgeNoteOffAsync(VAccent, cancellationToken);
            await client.SoundBridgeSetEffectAsync(VAccent, FxOff, 0, 0, cancellationToken);
            await client.SoundBridgeSetEffectAsync(VLead, FxOff, 0, 0, cancellationToken);
            await client.SoundBridgeSetEffectAsync(VBass, FxOff, 0, 0, cancellationToken);
        }

        await client.SoundBridgeStopAllAsync(cancellationToken);
        await client.WriteAtAsync(0, 22, "'NEON DRIFT' FINISHED.                         ", Rift64Color.Cyan, cancellationToken);
        await client.WriteAtAsync(0, 23, "                                              ", Rift64Color.Yellow, cancellationToken);
    }

    // Plays one bar: re-arms the arpeggiated chord on V3, runs a driving
    // eighth-note octave bass on V2, and an optional 8-step lead melody on V1.
    // Persistent PWM (bass) and vibrato (lead) are armed by the caller and keep
    // running. Returns true if the user aborted playback.
    private async Task<bool> PlayBarAsync(
        Rift64ProtocolClient client,
        CancellationToken cancellationToken,
        (ushort Root, bool Minor, ushort Bass, ushort BassOct) chord,
        ushort[]? melody,
        int stepMs,
        byte arpHold)
    {
        // (Re)arm the chord for this bar. The Note On stabs the root; the
        // arpeggio then cycles root -> 3rd -> 5th for the whole bar.
        await client.SoundBridgeNoteOnAsync(VAccent, chord.Root, instrumentId: 3, cancellationToken);
        await client.SoundBridgeSetArpeggioAsync(VAccent, holdFrames: arpHold, minor: chord.Minor, cancellationToken);

        for (int step = 0; step < 8; step++)
        {
            if (await AbortedAsync(client, cancellationToken)) return true;

            // Driving eighth-note octave bass on V2 (PWM colours the timbre).
            await client.SoundBridgeNoteOnAsync(VBass, (step & 1) == 0 ? chord.Bass : chord.BassOct, instrumentId: 2, cancellationToken);

            // Lead melody on V1 (0 = rest -> let the previous note ring).
            ushort m = melody is null ? (ushort)0 : melody[step];
            if (m != 0)
                await client.SoundBridgeNoteOnAsync(VLead, m, instrumentId: 1, cancellationToken);

            await Task.Delay(stepMs, cancellationToken);
        }
        return false;
    }

    // =====================================================================
    // Isolated effect demos
    // =====================================================================
    private async Task PlayVibratoSoloAsync(Rift64ProtocolClient client, CancellationToken cancellationToken)
    {
        await client.WriteAtAsync(0, 22, "VIBRATO SOLO ON V1 (SAW)...                   ", Rift64Color.LightGreen, cancellationToken);
        ushort[] phrase = { A4, C5, E5, C5, A4, B4, G4, A4 };
        foreach (var note in phrase)
        {
            if (cancellationToken.IsCancellationRequested) break;
            await client.SoundBridgeNoteOnAsync(VLead, note, instrumentId: 1, cancellationToken);
            // Deep, slow vibrato so it is unmistakable.
            await client.SoundBridgeSetEffectAsync(VLead, FxVibrato, speed: 0x1E, depth: 0x20, cancellationToken);
            await Task.Delay(700, cancellationToken);
            await client.SoundBridgeNoteOffAsync(VLead, cancellationToken);
            await Task.Delay(60, cancellationToken);
        }
        await client.SoundBridgeStopAllAsync(cancellationToken);
        await client.WriteAtAsync(0, 22, "VIBRATO SOLO DONE.                            ", Rift64Color.Cyan, cancellationToken);
    }

    private async Task PlayPwmSoloAsync(Rift64ProtocolClient client, CancellationToken cancellationToken)
    {
        await client.WriteAtAsync(0, 22, "PWM SOLO ON V2 (PULSE)...                     ", Rift64Color.LightGreen, cancellationToken);
        // Sustained pulse notes; the timbre sweeps as the pulse width modulates.
        ushort[] roots = { A3, C4, E4, A3 };
        foreach (var note in roots)
        {
            if (cancellationToken.IsCancellationRequested) break;
            await client.SoundBridgeNoteOnAsync(VBass, note, instrumentId: 2, cancellationToken);
            await client.SoundBridgeSetEffectAsync(VBass, FxPwm, speed: 0x14, depth: 0x60, cancellationToken);
            await Task.Delay(1600, cancellationToken);
            await client.SoundBridgeNoteOffAsync(VBass, cancellationToken);
            await Task.Delay(60, cancellationToken);
        }
        await client.SoundBridgeStopAllAsync(cancellationToken);
        await client.WriteAtAsync(0, 22, "PWM SOLO DONE.                                ", Rift64Color.Cyan, cancellationToken);
    }

    private async Task PlaySlideSoloAsync(Rift64ProtocolClient client, CancellationToken cancellationToken)
    {
        await client.WriteAtAsync(0, 22, "SLIDE (PORTAMENTO) ON V3 (PULSE)...           ", Rift64Color.LightGreen, cancellationToken);

        // Slide is portamento: each consecutive NoteOn glides from the prior
        // note. Speed gates the per-frame step rate; depth is unsigned SID
        // units per step. Direction is automatic from current vs. target.
        ushort[] phrase = { A3, A4, C4, A5, A3, E5 };

        await client.SoundBridgeNoteOnAsync(VAccent, phrase[0], instrumentId: 3, cancellationToken);
        await client.SoundBridgeSetEffectAsync(VAccent, FxSlide, speed: 0xC0, depth: 0x08, cancellationToken);
        await Task.Delay(700, cancellationToken);

        for (int i = 1; i < phrase.Length; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;
            await client.SoundBridgeNoteOnAsync(VAccent, phrase[i], instrumentId: 3, cancellationToken); // glides
            await Task.Delay(900, cancellationToken);
        }

        await client.SoundBridgeNoteOffAsync(VAccent, cancellationToken);
        await client.SoundBridgeSetEffectAsync(VAccent, FxOff, 0, 0, cancellationToken);

        await client.SoundBridgeStopAllAsync(cancellationToken);
        await client.WriteAtAsync(0, 22, "SLIDE SOLO DONE.                              ", Rift64Color.Cyan, cancellationToken);
    }

    private async Task PlayArpSoloAsync(Rift64ProtocolClient client, CancellationToken cancellationToken)
    {
        await client.WriteAtAsync(0, 22, "ARPEGGIO CHORDS ON V3 (PULSE)...              ", Rift64Color.LightGreen, cancellationToken);

        // Each entry: chord root + whether it is minor (true) or major (false).
        (ushort Root, bool Minor)[] progression =
        {
            (A3, true),   // Am
            (F4, false),  // F
            (C4, false),  // C
            (E4, true),   // Em
        };

        foreach (var (root, minor) in progression)
        {
            if (await AbortedAsync(client, cancellationToken)) return;
            await client.SoundBridgeNoteOnAsync(VAccent, root, instrumentId: 3, cancellationToken);
            // Fast hold (1 frame/tone) so root->3rd->5th fuse into a chord.
            await client.SoundBridgeSetArpeggioAsync(VAccent, holdFrames: 1, minor: minor, cancellationToken);
            await Task.Delay(900, cancellationToken);
            await client.SoundBridgeNoteOffAsync(VAccent, cancellationToken);
            await client.SoundBridgeSetEffectAsync(VAccent, FxOff, 0, 0, cancellationToken);
            await Task.Delay(60, cancellationToken);
        }

        await client.SoundBridgeStopAllAsync(cancellationToken);
        await client.WriteAtAsync(0, 22, "ARPEGGIO SOLO DONE.                           ", Rift64Color.Cyan, cancellationToken);
    }

    // Non-blocking abort: stops everything if a key is pressed mid-playback.
    private static async Task<bool> AbortedAsync(Rift64ProtocolClient client, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return true;
        var key = await client.ReadKeyAsync(TimeSpan.Zero, cancellationToken);
        if (key is null) return false;
        await client.SoundBridgeStopAllAsync(cancellationToken);
        await client.WriteAtAsync(0, 22, "PLAYBACK STOPPED BY USER.                     ", Rift64Color.Yellow, cancellationToken);
        await client.WriteAtAsync(0, 23, "                                              ", Rift64Color.Yellow, cancellationToken);
        return true;
    }
}
