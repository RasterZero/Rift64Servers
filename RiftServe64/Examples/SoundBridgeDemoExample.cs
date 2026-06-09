using RiftServe64.Sdk.Protocol;

/// <summary>
/// An interactive SID synthesizer driven by the SoundBridge command set,
/// turning the host keyboard into a three-voice instrument plus sound effects.
/// <para>
/// The SID chip has three independent voices, each with its own waveform,
/// ADSR envelope and pulse width. SoundBridge exposes them through high-level
/// commands: define instruments (<c>AD</c>), trigger Note On per voice, arm
/// per-voice effects (<c>AE</c>: vibrato/slide/PWM), and fire pre-uploaded SFX
/// scripts. This example also contrasts the two playback modes —
/// SOUNDBRIDGE_ONLY (all three voices keyboard-mapped) versus
/// MIXED_PLAYER_PLUS_SFX (a tracker owns V1/V2 while V3 stays free for SFX).
/// SFX scripts are uploaded to the safe $C000 region.
/// </para>
/// </summary>
public sealed class SoundBridgeDemoExample : IRift64MenuExample
{
    public char Key => 'G';
    public string MenuLabel => "SndBridge Synth";

    // Function key PETSCII values
    private const char KeyF1 = (char)133;
    private const char KeyF3 = (char)134;
    private const char KeyF5 = (char)135;
    private const char KeyF7 = (char)136;

    // Frequencies scale of 10 consecutive notes (C-4 to E-5)
    private static readonly ushort[] ScaleFreqs = new ushort[] {
        0x1167, // C-4  (0)
        0x1387, // D-4  (1)
        0x15E8, // E-4  (2)
        0x172E, // F-4  (3)
        0x1A09, // G-4  (4)
        0x1D43, // A-4  (5)
        0x211D, // B-4  (6)
        0x22CD, // C-5  (7)
        0x270E, // D-5  (8)
        0x2BD0  // E-5  (9)
    };

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
        await client.WriteAtAsync(0, 0, "EXAMPLE 11: SOUNDBRIDGE CAPABILITIES", Rift64Color.LightGreen, cancellationToken);

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

        // 4. Upload Bytecode SFX Scripts into dedicated RAM slots
        await client.WriteAtAsync(0, 5, "4. UPLOADING SFX SCRIPTS (C000)...", Rift64Color.White, cancellationToken);

        // Laser SFX script (ID 0, loaded at $C000)
        var laserScript = new byte[] {
            0x02, 0x00, 0x30, 0x00, 0x08, 0x01, 0x81, 0x21, // SET_FULL, saw + gate
            0x01, 0x02,                                     // WAIT 2
            0x03, 0x00, 0x24,                               // SET_FREQ
            0x01, 0x02,                                     // WAIT 2
            0x03, 0x00, 0x18,                               // SET_FREQ
            0x01, 0x02,                                     // WAIT 2
            0x03, 0x00, 0x10,                               // SET_FREQ
            0x07,                                           // GATE_OFF
            0x00                                            // END
        };
        await client.StoreMemoryCheckedAsync(0xC000, laserScript, cancellationToken);

        // Explosion SFX script (ID 1, loaded at $C040)
        var explosionScript = new byte[] {
            0x02, 0x00, 0x20, 0x00, 0x08, 0x02, 0xF8, 0x81, // SET_FULL, noise + gate
            0x01, 0x04,                                     // WAIT 4
            0x03, 0x00, 0x14,                               // SET_FREQ
            0x01, 0x04,                                     // WAIT 4
            0x03, 0x00, 0x08,                               // SET_FREQ
            0x01, 0x06,                                     // WAIT 6
            0x07,                                           // GATE_OFF
            0x00                                            // END
        };
        await client.StoreMemoryCheckedAsync(0xC040, explosionScript, cancellationToken);

        // Coin SFX script (ID 2, loaded at $C080)
        var coinScript = new byte[] {
            0x02, 0x00, 0x20, 0x00, 0x04, 0x01, 0x0F, 0x11, // SET_FULL, triangle wave
            0x01, 0x03,                                     // WAIT 3
            0x03, 0x00, 0x2C,                               // SET_FREQ higher
            0x01, 0x06,                                     // WAIT 6
            0x07,                                           // GATE_OFF
            0x00                                            // END
        };
        await client.StoreMemoryCheckedAsync(0xC080, coinScript, cancellationToken);

        // 5. Pre-load background music module for Mixed Mode demo
        var foundPath =
            ExampleAssets.Find("pkg/mw4title.bin") ??
            ExampleAssets.Find("musicmodule.bin") ??
            ExampleAssets.Find("tools/data/mw4title.bin");

        bool hasMusic = false;
        if (foundPath != null)
        {
            await client.WriteAtAsync(0, 6, "5. PRELOADING BACKGROUND MUSIC...", Rift64Color.White, cancellationToken);
            var moduleBytes = await File.ReadAllBytesAsync(foundPath, cancellationToken);
            moduleBytes = ExampleAssets.StripMiniPlayer2Header(moduleBytes);
            
            int offset = 0;
            bool uploadSuccess = true;
            while (offset < moduleBytes.Length)
            {
                int chunkSize = Math.Min(256, moduleBytes.Length - offset);
                var chunk = moduleBytes.AsMemory(offset, chunkSize);
                
                var chunkAck = await client.StoreMemoryCheckedAsync((ushort)(Rift64ProtocolClient.MiniPlayer2ModuleAddress + offset), chunk, cancellationToken);
                if (chunkAck != true)
                {
                    uploadSuccess = false;
                    break;
                }
                offset += chunkSize;
            }

            if (uploadSuccess)
            {
                await client.BindAudioModuleAsync(Rift64ProtocolClient.MiniPlayer2ModuleAddress, cancellationToken);
                hasMusic = true;
            }
        }

        // Set to SOUNDBRIDGE_ONLY mode initially (AM01)
        await client.SoundBridgeSetModeAsync(SoundBridgeAudioMode.SoundBridgeOnly, cancellationToken);
        byte currentMode = 1; // 1 = SoundBridge Only, 2 = Mixed Mode

        // Phase 3.1: the synthesizer UI is almost entirely static. Draw it once
        // up-front and only repaint when the audio mode actually toggles, instead
        // of re-sending ~18 lines of text over the serial link on every keypress.
        await client.ClearScreenAsync(cancellationToken);
        await DrawSynthUiAsync(client, currentMode, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            var key = await client.ReadKeyAsync(TimeSpan.FromMinutes(5), cancellationToken);
            if (key is null) continue;

            if (key is ' ')
            {
                break;
            }

            // 1-0: Play notes on Voice 0 (Instrument 1, Saw)
            if (currentMode == 1 && key >= '0' && key <= '9')
            {
                int noteIndex = (key.Value == '0') ? 9 : (key.Value - '1');
                ushort freq = ScaleFreqs[noteIndex];
                await client.WriteAtAsync(0, 22, $"PLAYING NOTE {key} (FREQ ${freq:X4}) ON V1...    ", Rift64Color.LightGreen, cancellationToken);
                await client.SoundBridgeNoteOnAsync(SoundBridgeVoice.Voice1, freq, instrumentId: 1, cancellationToken);
            }
            // Q-P: Play notes on Voice 1 (Instrument 2, Chime)
            else if (currentMode == 1 && (key is 'q' or 'Q' or 'w' or 'W' or 'e' or 'E' or 'r' or 'R' or 't' or 'T' or 'y' or 'Y' or 'u' or 'U' or 'i' or 'I' or 'o' or 'O' or 'p' or 'P'))
            {
                int noteIndex = MapQToPIndex(key.Value);
                if (noteIndex >= 0)
                {
                    ushort freq = ScaleFreqs[noteIndex];
                    await client.WriteAtAsync(0, 22, $"PLAYING NOTE {char.ToUpperInvariant(key.Value)} (FREQ ${freq:X4}) ON V2...", Rift64Color.LightGreen, cancellationToken);
                    await client.SoundBridgeNoteOnAsync(SoundBridgeVoice.Voice2, freq, instrumentId: 2, cancellationToken);
                }
            }
            // A-L: Play notes on Voice 2 (Instrument 3, Pulse)
            else if (currentMode == 1 && (key is 'a' or 'A' or 's' or 'S' or 'd' or 'D' or 'f' or 'F' or 'g' or 'G' or 'h' or 'H' or 'j' or 'J' or 'k' or 'K' or 'l' or 'L'))
            {
                int noteIndex = MapAToLIndex(key.Value);
                if (noteIndex >= 0)
                {
                    ushort freq = ScaleFreqs[noteIndex];
                    await client.WriteAtAsync(0, 22, $"PLAYING NOTE {char.ToUpperInvariant(key.Value)} (FREQ ${freq:X4}) ON V3...", Rift64Color.LightGreen, cancellationToken);
                    await client.SoundBridgeNoteOnAsync(SoundBridgeVoice.Voice3, freq, instrumentId: 3, cancellationToken);
                }
            }
            // F1 : Apply Vibrato to Voice 2 (V3)
            else if (key == KeyF1)
            {
                await client.WriteAtAsync(0, 22, "SETTING VIBRATO ON V3 (AE)...                  ", Rift64Color.LightGreen, cancellationToken);
                await client.SoundBridgeSetEffectAsync(SoundBridgeVoice.Voice3, SoundBridgeEffect.Vibrato, speed: 0x30, depth: 0x20, cancellationToken);
            }
            // F3 : Apply Slide Down to Voice 2 (V3)
            else if (key == KeyF3)
            {
                await client.WriteAtAsync(0, 22, "SETTING SLIDE DOWN ON V3 (AE)...               ", Rift64Color.LightGreen, cancellationToken);
                await client.SoundBridgeSetEffectAsync(SoundBridgeVoice.Voice3, SoundBridgeEffect.Slide, speed: 0x40, depth: 0xCE, cancellationToken);
            }
            // F5 : Apply PWM to Voice 2 (V3)
            else if (key == KeyF5)
            {
                await client.WriteAtAsync(0, 22, "SETTING PWM ON V3 (AE)...                      ", Rift64Color.LightGreen, cancellationToken);
                await client.SoundBridgeSetEffectAsync(SoundBridgeVoice.Voice3, SoundBridgeEffect.Pwm, speed: 0x20, depth: 0x60, cancellationToken);
            }
            // F7 : Clear Effects on Voice 2 (V3)
            else if (key == KeyF7)
            {
                await client.WriteAtAsync(0, 22, "CLEARING EFFECTS ON V3 (AE)...                 ", Rift64Color.LightGreen, cancellationToken);
                await client.SoundBridgeSetEffectAsync(SoundBridgeVoice.Voice3, SoundBridgeEffect.Off, speed: 0, depth: 0, cancellationToken);
            }
            // Z: Laser SFX (ID 0, Priority 3)
            else if (key is 'z' or 'Z')
            {
                await client.WriteAtAsync(0, 22, "TRIGGERING LASER SFX (SLOT 0) ON V3...        ", Rift64Color.LightGreen, cancellationToken);
                await client.SoundBridgePlaySfxAsync(sfxId: 0, priority: 3, flags: 0, cancellationToken);
            }
            // X: Explosion SFX (ID 1, Priority 4)
            else if (key is 'x' or 'X')
            {
                await client.WriteAtAsync(0, 22, "TRIGGERING EXPLOSION SFX (SLOT 1) ON V3...    ", Rift64Color.LightGreen, cancellationToken);
                await client.SoundBridgePlaySfxAsync(sfxId: 1, priority: 4, flags: 0, cancellationToken);
            }
            // C: Coin SFX (ID 2, Priority 5)
            else if (key is 'c' or 'C')
            {
                await client.WriteAtAsync(0, 22, "TRIGGERING COIN SFX (SLOT 2) ON V3...         ", Rift64Color.LightGreen, cancellationToken);
                await client.SoundBridgePlaySfxAsync(sfxId: 2, priority: 5, flags: 0, cancellationToken);
            }
            // B: Stop/silence all sounds (AZ)
            else if (key is 'b' or 'B')
            {
                await client.WriteAtAsync(0, 22, "SILENCING ALL AUDIO (AZ)...                   ", Rift64Color.White, cancellationToken);
                await client.SoundBridgeStopAllAsync(cancellationToken);
            }
            // M: Toggle Mode
            else if (key is 'm' or 'M')
            {
                if (hasMusic)
                {
                    if (currentMode == 1)
                    {
                        // Transition to MIXED mode (mode 2)
                        await client.WriteAtAsync(0, 22, "SWITCHING TO MIXED MODE (AM02)...             ", Rift64Color.White, cancellationToken);
                        await client.SoundBridgeStopAllAsync(cancellationToken);
                        await client.SoundBridgeSetModeAsync(SoundBridgeAudioMode.MixedPlayerPlusSfx, cancellationToken);
                        await client.StartAudioAsync(1, cancellationToken);
                        currentMode = 2;
                        await DrawSynthUiAsync(client, currentMode, cancellationToken);
                    }
                    else
                    {
                        // Transition to SOUNDBRIDGE_ONLY mode (mode 1)
                        await client.WriteAtAsync(0, 22, "SWITCHING TO SOUNDBRIDGE ONLY (AM01)...       ", Rift64Color.White, cancellationToken);
                        await client.SoundBridgeSetModeAsync(SoundBridgeAudioMode.SoundBridgeOnly, cancellationToken);
                        currentMode = 1;
                        await DrawSynthUiAsync(client, currentMode, cancellationToken);
                    }
                }
            }
        }

        // Clean up SID before exiting
        await client.SoundBridgeResetAsync(cancellationToken);
        await client.ClearScreenAsync(cancellationToken);
    }

    // Phase 3.1: paints the static synthesizer UI. Only the mode-dependent block
    // (rows 2-3, 7-9) and the bottom status line (row 22) ever change, so this is
    // invoked once at startup and again only when the audio mode toggles.
    private static async Task DrawSynthUiAsync(Rift64ProtocolClient client, byte currentMode, CancellationToken cancellationToken)
    {
        await client.WriteAtAsync(0, 0, "RIFT64 SOUNDBRIDGE SYNTHESIZER", Rift64Color.LightGreen, cancellationToken);

        if (currentMode == 1)
        {
            await client.WriteAtAsync(0, 2, "MODE: SOUNDBRIDGE_ONLY (AM01)          ", Rift64Color.Yellow, cancellationToken);
            await client.WriteAtAsync(0, 3, "SID Voice 1, 2 and 3 are mapped to keys.", Rift64Color.White, cancellationToken);
        }
        else
        {
            await client.WriteAtAsync(0, 2, "MODE: MIXED_PLAYER_PLUS_SFX (AM02)     ", Rift64Color.LightBlue, cancellationToken);
            await client.WriteAtAsync(0, 3, "Music plays on V1 & V2. SFX owns V3.", Rift64Color.White, cancellationToken);
        }

        await client.WriteAtAsync(0, 5, "KEYBOARD SYNTHESIZER:", Rift64Color.Cyan, cancellationToken);

        if (currentMode == 1)
        {
            await client.WriteAtAsync(0, 7, "1-0) Play Note scale on Voice 1 (Saw)", Rift64Color.White, cancellationToken);
            await client.WriteAtAsync(0, 8, "Q-P) Play Note scale on Voice 2 (Bell)", Rift64Color.White, cancellationToken);
            await client.WriteAtAsync(0, 9, "A-L) Play Note scale on Voice 3 (Pulse)", Rift64Color.White, cancellationToken);
        }
        else
        {
            await client.WriteAtAsync(0, 7, "[Tracker player owns Voice 1 and 2]      ", Rift64Color.MediumGray, cancellationToken);
            await client.WriteAtAsync(0, 8, "[V1 & V2 keyboard synthesis disabled]  ", Rift64Color.MediumGray, cancellationToken);
            await client.WriteAtAsync(0, 9, "[V3 available strictly for SFX]         ", Rift64Color.MediumGray, cancellationToken);
        }

        await client.WriteAtAsync(0, 11, "SOUND EFFECTS & ACTIONS:", Rift64Color.Cyan, cancellationToken);
        await client.WriteAtAsync(0, 13, "Z) Play Laser SFX      X) Play Explosion SFX", Rift64Color.White, cancellationToken);
        await client.WriteAtAsync(0, 14, "C) Play Coin Chirp     M) Toggle Audio Mode", Rift64Color.White, cancellationToken);
        await client.WriteAtAsync(0, 15, "B) Stop All Sounds", Rift64Color.White, cancellationToken);
        await client.WriteAtAsync(0, 16, "F1) Enable V3 Vibrato  F3) Enable V3 Slide", Rift64Color.White, cancellationToken);
        await client.WriteAtAsync(0, 17, "F5) Enable V3 PWM      F7) Clear V3 Effects", Rift64Color.White, cancellationToken);
        await client.WriteAtAsync(0, 19, "Space) Return to Menu", Rift64Color.White, cancellationToken);
    }

    private static int MapQToPIndex(char k)
    {
        return char.ToLowerInvariant(k) switch {
            'q' => 0, 'w' => 1, 'e' => 2, 'r' => 3, 't' => 4,
            'y' => 5, 'u' => 6, 'i' => 7, 'o' => 8, 'p' => 9,
            _ => -1
        };
    }

    private static int MapAToLIndex(char k)
    {
        return char.ToLowerInvariant(k) switch {
            'a' => 0, 's' => 1, 'd' => 2, 'f' => 3, 'g' => 4,
            'h' => 5, 'j' => 6, 'k' => 7, 'l' => 8,
            _ => -1
        };
    }
}