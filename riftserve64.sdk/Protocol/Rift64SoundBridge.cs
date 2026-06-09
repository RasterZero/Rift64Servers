namespace RiftServe64.Sdk.Protocol;

/// <summary>
/// SoundBridge audio routing mode (command <c>AM</c>). Selects who owns the
/// SID between the streamed song player and the SoundBridge note/SFX engine.
/// </summary>
public enum SoundBridgeAudioMode : byte
{
    /// <summary>Only the streamed song player drives the SID.</summary>
    PlayerOnly = 0,

    /// <summary>Only SoundBridge (manual notes + SFX) drives the SID.</summary>
    SoundBridgeOnly = 1,

    /// <summary>Song player on voices 1-2 with SoundBridge SFX layered on voice 3.</summary>
    MixedPlayerPlusSfx = 2,

    /// <summary>Direct manual SID register writes; no automatic engine.</summary>
    DirectSidManual = 3,
}

/// <summary>
/// Per-voice note effect applied by the SoundBridge engine (command <c>AE</c>).
/// Effects persist across NoteOns until changed or set to <see cref="Off"/>.
/// </summary>
public enum SoundBridgeEffect : byte
{
    /// <summary>No effect; the voice plays its raw note.</summary>
    Off = 0,

    /// <summary>Periodic pitch wobble. speed = rate, depth = amount.</summary>
    Vibrato = 1,

    /// <summary>Portamento: each new note glides from the previous pitch. speed = glide rate.</summary>
    Slide = 2,

    /// <summary>Pulse-width modulation; only audible on a PULSE-waveform voice. speed = rate, depth = amount.</summary>
    Pwm = 3,

    /// <summary>Chord arpeggio cycling root/third/fifth. speed = hold frames, depth = 1 for minor.</summary>
    Arpeggio = 4,
}

/// <summary>
/// SoundBridge voice index (0-2). Voice 3 (<see cref="Voice3"/>) is the SFX
/// voice in <see cref="SoundBridgeAudioMode.MixedPlayerPlusSfx"/>.
/// </summary>
public enum SoundBridgeVoice : byte
{
    /// <summary>SID voice 1 (index 0).</summary>
    Voice1 = 0,

    /// <summary>SID voice 2 (index 1).</summary>
    Voice2 = 1,

    /// <summary>SID voice 3 (index 2).</summary>
    Voice3 = 2,
}

/// <summary>
/// SID voice control-register ($D404/$D40B/$D412) bit flags. Combine a single
/// waveform bit with <see cref="Gate"/> to start a note, e.g.
/// <c>SidWaveform.Pulse | SidWaveform.Gate</c>.
/// </summary>
[System.Flags]
public enum SidWaveform : byte
{
    /// <summary>No bits set (silence / gate off).</summary>
    None = 0x00,

    /// <summary>Gate bit: start attack when set, release when cleared.</summary>
    Gate = 0x01,

    /// <summary>Hard-sync this voice's oscillator to the previous voice.</summary>
    Sync = 0x02,

    /// <summary>Ring-modulate the triangle output with the previous voice.</summary>
    RingMod = 0x04,

    /// <summary>Test bit: reset and lock the oscillator.</summary>
    Test = 0x08,

    /// <summary>Triangle waveform.</summary>
    Triangle = 0x10,

    /// <summary>Sawtooth waveform.</summary>
    Sawtooth = 0x20,

    /// <summary>Pulse (square) waveform; width set via pulse-width.</summary>
    Pulse = 0x40,

    /// <summary>Noise waveform.</summary>
    Noise = 0x80,
}
