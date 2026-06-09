using RiftServe64.Sdk.Protocol;

/// <summary>
/// Demonstrates the most fundamental RIFT64 operation: pushing text and a
/// PETSCII box border onto the C64's 40x25 text screen at $0400.
/// <para>
/// On a stock C64 the VIC-II reads the 1000-byte screen matrix at $0400 and a
/// parallel 1000-nybble Color RAM at $D800; each cell is one screen code plus a
/// 4-bit colour. This example uses the high-level <c>L</c> (length-prefixed text)
/// and <c>E</c> (erase line) protocol commands so the firmware performs the
/// PETSCII conversion and cursor advance, illustrating the simplest path from a
/// modern host to glyphs on glass.
/// </para>
/// </summary>
public sealed class ConnectionBannerExample : IRift64MenuExample
{
    public char Key => '1';
    public string MenuLabel => "Banner & Text";

    public async Task RunAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        await client.ClearScreenAsync(cancellationToken);

        await client.SetCursorAsync(0, 0, cancellationToken);
        await client.DrawBorderAsync(40, 8, Rift64BorderGlyphs.Default, cancellationToken);

        await client.WriteAtAsync(2, 1, "EXAMPLE 1: L + E TEXT OPS", Rift64Color.LightGreen, cancellationToken);
        await client.SetCursorAsync(2, 3, cancellationToken);
        await client.WriteLengthTextAsync("LENGTH PREFIXED TEXT IS WORKING.", cancellationToken);

        await client.WriteAtAsync(2, 5, "This tail is erased -> ********", Rift64Color.White, cancellationToken);
        await client.SetCursorAsync(26, 5, cancellationToken);
        await client.EraseLineAsync(8, cancellationToken);

        await client.WriteAtAsync(0, 10, "Press any key to return to menu.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);
    }
}
