using RiftServe64.Sdk.Protocol;

/// <summary>
/// Demonstrates streaming a SID music module to the client and driving the
/// MiniPlayer2 playback engine.
/// <para>
/// The C64's SID (6581/8580) is driven by the resident MiniPlayer2 routine,
/// which expects a page-aligned music module. This example uploads the raw
/// module in 256-byte checksum-verified chunks to the safe address
/// <see cref="Rift64ProtocolClient.MiniPlayer2ModuleAddress"/> ($7000), binds it
/// (<c>A5</c>), sets volume, and starts a subtune (<c>A1</c>) — then exposes
/// pause/resume/stop transport. All SID register pokes happen on the client; the
/// host only sends high-level <c>A</c> audio commands.
/// </para>
/// </summary>
public sealed class AudioPlayerExample : IRift64MenuExample
{
    public char Key => 'A';
    public string MenuLabel => "SID Music";

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

    // Phase 2.3: best-effort restoration that silences the SID so audio never
    // keeps playing after this demo faults or is cancelled mid-playback.
    private static async Task RestoreClientStateAsync(Rift64ProtocolClient client)
    {
        try
        {
            await client.StopAudioAsync(CancellationToken.None);
        }
        catch
        {
            // The connection may already be gone during shutdown; ignore.
        }
    }

    private async Task RunCoreAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 10: SID MUSIC PLAYER (A)", Rift64Color.LightGreen, cancellationToken);

        // 1. Locate and read the precompiled SID music module
        var foundPath =
            ExampleAssets.Find("pkg/mw4title.bin") ??
            ExampleAssets.Find("musicmodule.bin") ??
            ExampleAssets.Find("tools/data/mw4title.bin");

        if (foundPath == null)
        {
            await client.WriteAtAsync(0, 2, "ERROR: musicmodule.bin NOT FOUND!", Rift64Color.Red, cancellationToken);
            await client.WriteAtAsync(0, 4, "PRESS ANY KEY TO RETURN.", Rift64Color.Yellow, cancellationToken);
            await client.PauseForKeyAsync(cancellationToken: cancellationToken);
            return;
        }

        await client.WriteAtAsync(0, 2, $"1. LOADING: {Path.GetFileName(foundPath)}", Rift64Color.White, cancellationToken);
        var moduleBytes = await File.ReadAllBytesAsync(foundPath, cancellationToken);
        moduleBytes = ExampleAssets.StripMiniPlayer2Header(moduleBytes);

        // 2. Stop player first
        await client.WriteAtAsync(0, 4, "2. STOPPING PRIOR PLAYBACK (A0)...", Rift64Color.White, cancellationToken);
        await client.StopAudioAsync(cancellationToken);

        // 3. Upload raw SID module in chunks of up to 256 bytes
        await client.WriteAtAsync(0, 6, $"3. UPLOADING {moduleBytes.Length} BYTES TO ${Rift64ProtocolClient.MiniPlayer2ModuleAddress:X4}...", Rift64Color.White, cancellationToken);
        
        int offset = 0;
        bool uploadSuccess = true;
        while (offset < moduleBytes.Length)
        {
            int chunkSize = Math.Min(256, moduleBytes.Length - offset);
            var chunk = moduleBytes.AsMemory(offset, chunkSize);
            
            // Print progress
            int percent = (offset + chunkSize) * 100 / moduleBytes.Length;
            await client.WriteAtAsync(0, 7, $"UPLOADING CHUNK: {percent}%                   ", Rift64Color.Cyan, cancellationToken);
            
            var chunkAck = await client.StoreMemoryCheckedAsync((ushort)(Rift64ProtocolClient.MiniPlayer2ModuleAddress + offset), chunk, cancellationToken);
            if (chunkAck != true)
            {
                uploadSuccess = false;
                break;
            }
            
            offset += chunkSize;
        }

        if (!uploadSuccess)
        {
            await client.WriteAtAsync(0, 8, "UPLOAD FAILED or TIMED OUT!", Rift64Color.Red, cancellationToken);
            await client.WriteAtAsync(0, 10, "PRESS ANY KEY TO RETURN.", Rift64Color.Yellow, cancellationToken);
            await client.PauseForKeyAsync(cancellationToken: cancellationToken);
            return;
        }

        // 4. Bind the module at the player's hard-coded module address
        await client.WriteAtAsync(0, 8, "4. BINDING MODULE TO PLAYER (A5)...", Rift64Color.White, cancellationToken);
        var bindAck = await client.BindAudioModuleAsync(Rift64ProtocolClient.MiniPlayer2ModuleAddress, cancellationToken);

        // 5. Set Volume to Max (15)
        await client.WriteAtAsync(0, 10, "5. SETTING VOLUME TO 15 (A6)...", Rift64Color.White, cancellationToken);
        var volAck = await client.SetAudioVolumeAsync(15, cancellationToken);

        // 6. Start Playback of Subtune 1 (A1)
        await client.WriteAtAsync(0, 12, "6. STARTING SUBTUNE 1 (A1)...", Rift64Color.White, cancellationToken);
        var playAck = await client.StartAudioAsync(1, cancellationToken);

        // Success message
        await client.WriteAtAsync(0, 14, "PLAYING SID TUNE SUCCESSFULLY!", Rift64Color.LightGreen, cancellationToken);

        // Phase 3.1: the transport help line never changes, so draw it once
        // before the loop instead of re-sending it on every keypress.
        await client.WriteAtAsync(0, 16, "P) PAUSE   R) RESUME   S) STOP   Q) QUIT", Rift64Color.Cyan, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            var key = await client.ReadKeyAsync(TimeSpan.FromMinutes(5), cancellationToken);
            if (key is null) continue;

            if (key is 'q' or 'Q')
            {
                break;
            }

            if (key is 'p' or 'P')
            {
                await client.WriteAtAsync(0, 18, "PAUSING MUSIC (A2)...                   ", Rift64Color.White, cancellationToken);
                await client.PauseAudioAsync(cancellationToken);
            }
            else if (key is 'r' or 'R')
            {
                await client.WriteAtAsync(0, 18, "RESUMING MUSIC (A3)...                  ", Rift64Color.White, cancellationToken);
                await client.ResumeAudioAsync(cancellationToken);
            }
            else if (key is 's' or 'S')
            {
                await client.WriteAtAsync(0, 18, "STOPPING MUSIC (A0)...                  ", Rift64Color.White, cancellationToken);
                await client.StopAudioAsync(cancellationToken);
            }
        }

        // Ensure we silence the SID when quitting
        await client.StopAudioAsync(cancellationToken);

        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 10: COMPLETE", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(0, 2, "MUSIC PLAYER COMPLETED.", Rift64Color.White, cancellationToken);
        await client.WriteAtAsync(0, 4, "PRESS ANY KEY TO RETURN TO MENU.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);
    }
}
