using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RiftServe64.Sdk.Protocol;

/// <summary>
/// SFX bytecode builders for classic SID percussion. A drum is an SFX script
/// (64-byte slot) the client's interpreter runs on any voice, triggered
/// directly (<c>AS</c>) or from a tracker row. Upload a kit once per session
/// with <see cref="UploadDrumKitAsync"/>.
/// </summary>
public static class SidDrumKit
{
    /// <summary>SFX bytecode opcodes understood by the client interpreter.</summary>
    public const byte OpEnd = 0x00;
    public const byte OpWait = 0x01;          // + frames
    public const byte OpSetFull = 0x02;       // + FL FH PL PH AD SR CT
    public const byte OpSetFreq = 0x03;       // + FL FH
    public const byte OpSetCtrl = 0x04;       // + CT
    public const byte OpSetAdsr = 0x05;       // + AD SR
    public const byte OpSetPulse = 0x06;      // + PL PH
    public const byte OpGateOff = 0x07;
    public const byte OpPitchSlide = 0x08;    // + frames deltaLo deltaHi (signed 16-bit per frame)
    public const byte OpSetFilter = 0x09;     // + cutoffHi resRoute modeBits (volume nibble preserved)
    public const byte OpRestart = 0x0A;       // loop to the slot start (implies a 1-frame wait)

    /// <summary>Slot size and count of the client SFX bank.</summary>
    public const int SlotSize = 64;
    public const int SlotCount = 16;

    /// <summary>
    /// Bass drum: one frame of noise click, then a fast triangle pitch drop
    /// from $0800. Roughly 10 frames long.
    /// </summary>
    public static byte[] Kick() =>
    [
        OpSetFull, 0x00, 0x30, 0x00, 0x08, 0x08, 0x00, 0x81,  // noise burst, A0 D8
        OpWait, 1,
        OpSetFreq, 0x00, 0x08,                                // $0800
        OpSetCtrl, 0x11,                                      // triangle + gate
        OpPitchSlide, 8, 0x20, 0xFF,                          // -224/frame for 8 frames
        OpGateOff,
        OpEnd,
    ];

    /// <summary>Snare: noise burst stepping down in pitch over ~5 frames.</summary>
    public static byte[] Snare() =>
    [
        OpSetFull, 0x00, 0x40, 0x00, 0x08, 0x09, 0x00, 0x81,  // noise, A0 D9
        OpWait, 2,
        OpSetFreq, 0x00, 0x28,
        OpWait, 2,
        OpSetFreq, 0x00, 0x18,
        OpWait, 1,
        OpGateOff,
        OpEnd,
    ];

    /// <summary>Closed hi-hat: a single frame of bright noise.</summary>
    public static byte[] HiHatClosed() =>
    [
        OpSetFull, 0x00, 0xC8, 0x00, 0x08, 0x02, 0x00, 0x81,
        OpWait, 1,
        OpGateOff,
        OpEnd,
    ];

    /// <summary>Open hi-hat: bright noise with a longer decay (~6 frames).</summary>
    public static byte[] HiHatOpen() =>
    [
        OpSetFull, 0x00, 0xC8, 0x00, 0x08, 0x06, 0x00, 0x81,
        OpWait, 5,
        OpGateOff,
        OpEnd,
    ];

    /// <summary>
    /// Tom: a triangle pitch drop starting at <paramref name="pitchHi"/>
    /// (high byte of the SID frequency; e.g. $06 low tom, $0A high tom).
    /// </summary>
    public static byte[] Tom(byte pitchHi = 0x06) =>
    [
        OpSetFull, 0x00, pitchHi, 0x00, 0x08, 0x09, 0x00, 0x11, // triangle + gate
        OpPitchSlide, 10, 0xC0, 0xFF,                           // -64/frame for 10 frames
        OpGateOff,
        OpEnd,
    ];

    /// <summary>A standard 5-piece kit for slots 0-4: kick, snare, closed hat, open hat, tom.</summary>
    public static IReadOnlyList<byte[]> StandardKit() =>
        [Kick(), Snare(), HiHatClosed(), HiHatOpen(), Tom()];

    /// <summary>
    /// Uploads <paramref name="drums"/> into consecutive SFX slots starting
    /// at slot 0 of the bank page <paramref name="basePage"/> (e.g. $C0 for
    /// $C000) and points the client's SFX base at it. Trigger afterwards
    /// with <c>SoundBridgePlaySfxAsync(slot, priority, voice)</c> or from
    /// tracker rows via <c>SetDrum</c>.
    /// </summary>
    public static async Task<bool> UploadDrumKitAsync(
        this Rift64ProtocolClient client,
        byte basePage,
        IReadOnlyList<byte[]> drums,
        CancellationToken cancellationToken = default)
    {
        if (drums.Count is < 1 or > SlotCount)
            throw new ArgumentException($"A kit holds 1-{SlotCount} drums.", nameof(drums));

        var bank = new byte[drums.Count * SlotSize];
        for (var i = 0; i < drums.Count; i++)
        {
            if (drums[i].Length > SlotSize)
                throw new ArgumentException($"Drum {i} is {drums[i].Length} bytes; slots hold {SlotSize}.", nameof(drums));
            drums[i].CopyTo(bank, i * SlotSize);
        }

        var uploaded = await client.StoreMemoryLargeCheckedAsync(
            (ushort)(basePage << 8), bank, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!uploaded) return false;

        var based = await client.SoundBridgeSetSfxBaseAsync(basePage, cancellationToken).ConfigureAwait(false);
        return based == true;
    }
}
