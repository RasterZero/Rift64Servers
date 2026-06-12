using System;
using System.Threading;
using System.Threading.Tasks;
using RiftServe64.Sdk.Protocol;

/// <summary>
/// Demonstrates the native protocol-level hardware memory-copy command ('O')
/// copying 1K data blocks locally on the C64 from high RAM ($C000-$D000)
/// directly to active Screen RAM ($0400).
/// </summary>
public sealed class MemoryCopyExample : IRift64MenuExample
{
    public char Key => 'J';
    public string MenuLabel => "Hardware Memory Copy";

    public async Task RunAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        await client.ClearScreenAsync(cancellationToken);
        await client.SetColorsAsync(Rift64Color.Blue, Rift64Color.White, cancellationToken);
        await client.SetCursorVisibilityAsync(false, cancellationToken);

        await client.WriteAtAsync(2, 5, "RIFT64 PROTOCOL MEMORY COPY TEST", Rift64Color.Yellow, cancellationToken);
        await client.WriteAtAsync(2, 7, "UPLOADING 1K BLOCKS TO RAM...", Rift64Color.LightGreen, cancellationToken);

        // Define 1000-byte blocks (matching 40x25 screen size)
        const ushort screenBytesCount = 1000;
        var blockA = new byte[screenBytesCount]; Array.Fill(blockA, (byte)1); // Screen code 1 = 'A'
        var blockB = new byte[screenBytesCount]; Array.Fill(blockB, (byte)2); // Screen code 2 = 'B'
        var blockC = new byte[screenBytesCount]; Array.Fill(blockC, (byte)3); // Screen code 3 = 'C'
        var blockD = new byte[screenBytesCount]; Array.Fill(blockD, (byte)4); // Screen code 4 = 'D'

        // Memory locations: unshadowed high RAM pages
        ushort addrA = 0xC000;
        ushort addrB = 0xC400;
        ushort addrC = 0xC800;
        ushort addrD = 0xCC00;

        await SafeStoreLargeMemoryAsync(client, addrA, blockA, cancellationToken);
        await SafeStoreLargeMemoryAsync(client, addrB, blockB, cancellationToken);
        await SafeStoreLargeMemoryAsync(client, addrC, blockC, cancellationToken);
        await SafeStoreLargeMemoryAsync(client, addrD, blockD, cancellationToken);

        // Paint the entire Color RAM at $D800 to Yellow so characters are bright and readable
        var colors = new byte[screenBytesCount];
        Array.Fill(colors, (byte)Rift64Color.Yellow);
        await SafeStoreLargeMemoryAsync(client, 0xD800, colors, cancellationToken);

        await client.WriteAtAsync(2, 10, "UPLOADS DONE. TEST COMMENCING IN 2S...", Rift64Color.Cyan, cancellationToken);
        await Task.Delay(2000, cancellationToken);

        // Loop copies
        for (int run = 1; run <= 3 && !cancellationToken.IsCancellationRequested; run++)
        {
            await client.CopyMemoryAsync(addrA, 0x0400, screenBytesCount, cancellationToken);
            await Task.Delay(2000, cancellationToken);

            await client.CopyMemoryAsync(addrB, 0x0400, screenBytesCount, cancellationToken);
            await Task.Delay(2000, cancellationToken);

            await client.CopyMemoryAsync(addrC, 0x0400, screenBytesCount, cancellationToken);
            await Task.Delay(2000, cancellationToken);

            await client.CopyMemoryAsync(addrD, 0x0400, screenBytesCount, cancellationToken);
            await Task.Delay(2000, cancellationToken);
        }

        // Restore screen
        await client.ClearScreenAsync(cancellationToken);
        await client.SetColorsAsync(Rift64Color.Blue, Rift64Color.White, cancellationToken);
        await client.SetCursorVisibilityAsync(true, cancellationToken);
        await client.WriteAtAsync(2, 10, "MEMORY COPY PROTOCOL TEST COMPLETED!", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(2, 12, "Press any key to return to menu.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);
    }

    private static async Task SafeStoreLargeMemoryAsync(Rift64ProtocolClient client, ushort startAddress, byte[] data, CancellationToken ct)
    {
        const int maxChunkSize = 250;
        int offset = 0;
        while (offset < data.Length)
        {
            int chunkSize = Math.Min(maxChunkSize, data.Length - offset);
            var chunk = new byte[chunkSize];
            Array.Copy(data, offset, chunk, 0, chunkSize);
            
            await client.StoreMemoryAsync((ushort)(startAddress + offset), chunk, ct);
            offset += chunkSize;
        }
    }
}
