using System;
using System.Threading;
using System.Threading.Tasks;

namespace RiftServe64.Sdk.Protocol;

/// <summary>
/// Strongly-typed, enum-based overloads for the SoundBridge audio API. These
/// forward to the underlying byte-based methods so existing callers keep
/// working, while new code can use <see cref="SoundBridgeVoice"/>,
/// <see cref="SoundBridgeAudioMode"/>, <see cref="SoundBridgeEffect"/> and
/// <see cref="SidWaveform"/> instead of magic numbers.
/// </summary>
public sealed partial class Rift64ProtocolClient
{
    // --- Mode (AM) -------------------------------------------------------

    public Task<bool?> SoundBridgeSetModeAsync(SoundBridgeAudioMode mode, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SoundBridgeSetModeAsync((byte)mode, timeout, cancellationToken);

    public Task<bool?> SoundBridgeSetModeAsync(SoundBridgeAudioMode mode, CancellationToken cancellationToken = default) =>
        SoundBridgeSetModeAsync((byte)mode, cancellationToken);

    // --- Define instrument (AI) -----------------------------------------

    public Task<bool?> SoundBridgeDefineInstrumentAsync(byte id, ushort pulseWidth, byte attackDecay, byte sustainRelease, SidWaveform control, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SoundBridgeDefineInstrumentAsync(id, pulseWidth, attackDecay, sustainRelease, (byte)control, timeout, cancellationToken);

    public Task<bool?> SoundBridgeDefineInstrumentAsync(byte id, ushort pulseWidth, byte attackDecay, byte sustainRelease, SidWaveform control, CancellationToken cancellationToken = default) =>
        SoundBridgeDefineInstrumentAsync(id, pulseWidth, attackDecay, sustainRelease, (byte)control, cancellationToken);

    // --- Note on/off (AN/AK/AO) -----------------------------------------

    public Task SoundBridgeNoteOnAsync(SoundBridgeVoice voice, ushort sidFrequency, byte instrumentId, CancellationToken cancellationToken = default) =>
        SoundBridgeNoteOnAsync((byte)voice, sidFrequency, instrumentId, cancellationToken);

    public Task SoundBridgeNoteOnIndexAsync(SoundBridgeVoice voice, byte noteIndex, byte instrumentId, CancellationToken cancellationToken = default) =>
        SoundBridgeNoteOnIndexAsync((byte)voice, noteIndex, instrumentId, cancellationToken);

    /// <summary>Note on by name, e.g. <c>"C-4"</c> or <c>"A#3"</c> (octaves 0-7).</summary>
    public Task SoundBridgeNoteOnAsync(SoundBridgeVoice voice, string note, byte instrumentId, CancellationToken cancellationToken = default) =>
        SoundBridgeNoteOnIndexAsync((byte)voice, SidNote.Index(note), instrumentId, cancellationToken);

    public Task SoundBridgeNoteOffAsync(SoundBridgeVoice voice, CancellationToken cancellationToken = default) =>
        SoundBridgeNoteOffAsync((byte)voice, cancellationToken);

    // --- Full voice setup (AF) ------------------------------------------

    public Task SoundBridgeFullVoiceSetupAsync(SoundBridgeVoice voice, ushort sidFrequency, ushort pulseWidth, byte attackDecay, byte sustainRelease, SidWaveform control, CancellationToken cancellationToken = default) =>
        SoundBridgeFullVoiceSetupAsync((byte)voice, sidFrequency, pulseWidth, attackDecay, sustainRelease, (byte)control, cancellationToken);

    // --- Frequency / ADSR / pulse width (AQ/AD/AP) ----------------------

    public Task SoundBridgeSetFrequencyAsync(SoundBridgeVoice voice, ushort sidFrequency, CancellationToken cancellationToken = default) =>
        SoundBridgeSetFrequencyAsync((byte)voice, sidFrequency, cancellationToken);

    public Task SoundBridgeSetAdsrAsync(SoundBridgeVoice voice, byte attackDecay, byte sustainRelease, CancellationToken cancellationToken = default) =>
        SoundBridgeSetAdsrAsync((byte)voice, attackDecay, sustainRelease, cancellationToken);

    public Task SoundBridgeSetPulseWidthAsync(SoundBridgeVoice voice, ushort pulseWidth, CancellationToken cancellationToken = default) =>
        SoundBridgeSetPulseWidthAsync((byte)voice, pulseWidth, cancellationToken);

    // --- Control / waveform (AW) ----------------------------------------

    public Task SoundBridgeSetControlAsync(byte voice, SidWaveform control, CancellationToken cancellationToken = default) =>
        SoundBridgeSetControlAsync(voice, (byte)control, cancellationToken);

    public Task SoundBridgeSetControlAsync(SoundBridgeVoice voice, SidWaveform control, CancellationToken cancellationToken = default) =>
        SoundBridgeSetControlAsync((byte)voice, (byte)control, cancellationToken);

    // --- Effects (AE) ----------------------------------------------------

    public Task SoundBridgeSetEffectAsync(byte voice, SoundBridgeEffect effect, byte speed, byte depth, CancellationToken cancellationToken = default) =>
        SoundBridgeSetEffectAsync(voice, (byte)effect, speed, depth, cancellationToken);

    public Task SoundBridgeSetEffectAsync(SoundBridgeVoice voice, SoundBridgeEffect effect, byte speed, byte depth, CancellationToken cancellationToken = default) =>
        SoundBridgeSetEffectAsync((byte)voice, (byte)effect, speed, depth, cancellationToken);

    public Task SoundBridgeSetArpeggioAsync(SoundBridgeVoice voice, byte holdFrames, bool minor, CancellationToken cancellationToken = default) =>
        SoundBridgeSetArpeggioAsync((byte)voice, holdFrames, minor, cancellationToken);
}
