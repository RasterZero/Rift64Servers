namespace RiftServe64.Sdk.Protocol;

/// <summary>
/// SoundBridge audio routing mode (command <c>AM</c>). Selects whether the
/// client audio engine (tracker + SFX + note effects) ticks each frame or
/// the host drives the SID registers directly.
/// </summary>
public enum SoundBridgeAudioMode : byte
{
    /// <summary>Legacy MiniPlayer2 mode; the client treats it as <see cref="SoundBridgeOnly"/>.</summary>
    [Obsolete("MiniPlayer2 was removed from the client; this value now selects the engine mode (same as SoundBridgeOnly).")]
    PlayerOnly = 0,

    /// <summary>The engine mode: tracker, SFX scripts and note effects run each jiffy.</summary>
    SoundBridgeOnly = 1,

    /// <summary>Legacy mixed mode; the client treats it as <see cref="SoundBridgeOnly"/>.</summary>
    [Obsolete("MiniPlayer2 was removed from the client; this value now selects the engine mode (same as SoundBridgeOnly).")]
    MixedPlayerPlusSfx = 2,

    /// <summary>Direct manual SID register writes; no automatic engine.</summary>
    DirectSidManual = 3,
}

/// <summary>
/// Per-voice note effect applied by the SoundBridge engine (command <c>AE</c>).
/// Effects persist across NoteOns until changed or set to <see cref="Off"/>.
/// A voice has one PITCH effect slot (vibrato / slide / arpeggio) plus an
/// independent pulse-LFO slot: setting <see cref="Pwm"/> does not disturb the
/// pitch effect, so send a pitch effect first and <see cref="Pwm"/> second to
/// run both concurrently. Setting a pitch effect clears the pulse LFO;
/// <see cref="Off"/> clears both.
/// </summary>
public enum SoundBridgeEffect : byte
{
    /// <summary>No effect; clears both the pitch effect and the pulse LFO.</summary>
    Off = 0,

    /// <summary>Periodic pitch wobble. speed = rate, depth = amount.</summary>
    Vibrato = 1,

    /// <summary>
    /// Portamento: each new note glides from the previous pitch, retriggering
    /// the envelope. speed = glide step rate, depth = step size.
    /// </summary>
    Slide = 2,

    /// <summary>
    /// Pulse-width LFO; only audible on a PULSE-waveform voice. speed = rate,
    /// depth = amount. Runs in its own slot, layered under any pitch effect.
    /// </summary>
    Pwm = 3,

    /// <summary>
    /// Chord arpeggio cycling three tones on one voice, in true semitones
    /// from the note table. speed = hold frames per tone; depth = semitone
    /// offsets packed as $xy (tone 2 = root + x, tone 3 = root + y).
    /// Legacy values: depth 0 = major (+4,+7), 1 = minor (+3,+7).
    /// </summary>
    Arpeggio = 4,

    /// <summary>
    /// Legato portamento: like <see cref="Slide"/> but a new note only
    /// retargets the glide -- the envelope is NOT retriggered, so tied
    /// phrases bend smoothly instead of re-plucking.
    /// </summary>
    SlideLegato = 5,
}

/// <summary>
/// SoundBridge voice index (0-2). Notes, effects and SFX/drum scripts can
/// target any of the three voices.
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
