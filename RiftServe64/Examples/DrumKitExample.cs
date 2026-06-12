using RiftServe64.Sdk.Protocol;

/// <summary>
/// Demonstrates SID percussion: a five-piece drum kit built from SFX
/// bytecode (<see cref="SidDrumKit"/>) is uploaded into the $C000 bank, then
/// triggered from the host keyboard on any of the three voices. Up to three
/// drums sound at once — one script context per voice — which the old
/// single-context, voice-3-only SFX engine could not do.
/// </summary>
public sealed class DrumKitExample : IRift64MenuExample
{
    public char Key => 'L';
    public string MenuLabel => "SID Drum Kit";

    private static readonly string[] DrumNames = ["KICK", "SNARE", "HAT CLOSED", "HAT OPEN", "TOM"];

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
                await client.SoundBridgeStopAllAsync(CancellationToken.None);
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
        await client.WriteAtAsync(0, 0, "EXAMPLE: SID DRUM KIT (SFX SCRIPTS)", Rift64Color.LightGreen, cancellationToken);

        await client.WriteAtAsync(0, 2, "UPLOADING KIT TO $C000 (AB)...", Rift64Color.White, cancellationToken);
        await client.SoundBridgeResetAsync(cancellationToken);
        await client.SetAudioVolumeAsync(15, cancellationToken);
        var kitOk = await client.UploadDrumKitAsync(0xC0, SidDrumKit.StandardKit(), cancellationToken);
        if (!kitOk)
        {
            await client.WriteAtAsync(0, 4, "UPLOAD FAILED!", Rift64Color.Red, cancellationToken);
            await client.PauseForKeyAsync(cancellationToken: cancellationToken);
            return;
        }

        await client.WriteAtAsync(0, 4, "DRUMS (PLAYED ROUND-ROBIN ON V1-V3):", Rift64Color.Cyan, cancellationToken);
        await client.WriteAtAsync(0, 6, "1) KICK     2) SNARE    3) HAT CLOSED", Rift64Color.White, cancellationToken);
        await client.WriteAtAsync(0, 7, "4) HAT OPEN 5) TOM", Rift64Color.White, cancellationToken);
        await client.WriteAtAsync(0, 9, "MASH KEYS - 3 DRUMS CAN OVERLAP.", Rift64Color.White, cancellationToken);
        await client.WriteAtAsync(0, 11, "Q) QUIT", Rift64Color.Cyan, cancellationToken);

        byte voice = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var key = await client.ReadKeyAsync(TimeSpan.FromMinutes(5), cancellationToken);
            if (key is null) continue;
            if (key is 'q' or 'Q') break;

            if (key is >= '1' and <= '5')
            {
                var slot = (byte)(key.Value - '1');
                await client.SoundBridgePlaySfxAsync(sfxId: slot, priority: 0x40, voice: voice, cancellationToken);
                await client.WriteAtAsync(0, 13, $"{DrumNames[slot],-10} ON VOICE {voice + 1}   ", Rift64Color.LightGreen, cancellationToken);
                voice = (byte)((voice + 1) % 3);
            }
        }

        await client.SoundBridgeStopAllAsync(cancellationToken);
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "DRUM KIT COMPLETE.", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(0, 2, "PRESS ANY KEY TO RETURN TO MENU.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);
    }
}
