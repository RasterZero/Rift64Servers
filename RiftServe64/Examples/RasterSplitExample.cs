using RiftServe64.Sdk.Protocol;

/// <summary>
/// Demonstrates a VIC-II raster split: changing display registers mid-frame so
/// the top and bottom of the screen use different character sets.
/// <para>
/// The VIC-II draws the screen one raster line at a time. By arming a raster
/// interrupt at a chosen scanline, the firmware can rewrite $D018 (the charset
/// pointer) partway down the frame, so rows above the split show the uppercase
/// ROM font and rows below show the lowercase font — all from a single screen
/// matrix. The <c>N</c> command configures the split line plus the
/// $D011/$D016/$D018 values for each half. This is the classic technique behind
/// multi-mode C64 screens.
/// </para>
/// </summary>
public sealed class RasterSplitExample : IRift64MenuExample
{
    public char Key => '9';
    public string MenuLabel => "Raster Split";

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

    // Phase 2.3: best-effort restoration that disables the raster split so the
    // host menu is not left with a mid-screen font change after a fault/cancel.
    private static async Task RestoreClientStateAsync(Rift64ProtocolClient client)
    {
        try
        {
            await client.SetRasterSplitAsync(
                enable: false,
                splitLine: 154,
                topD011: 0x1B, topD016: 0x08, topD018: 0x14,
                botD011: 0x1B, botD016: 0x08, botD018: 0x16,
                CancellationToken.None);
        }
        catch
        {
            // The connection may already be gone during shutdown; ignore.
        }
    }

    private async Task RunCoreAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 9: VIC-II RASTER SPLIT (N)", Rift64Color.LightGreen, cancellationToken);

        await client.WriteAtAsync(0, 2, "TOP HALF (EXPECTED: UPPERCASE)", Rift64Color.White, cancellationToken);
        for (byte row = 4; row < 12; row++)
        {
            await client.SetCursorAsync(2, row, cancellationToken);
            await client.WriteTextAsync($"ROW {row:D2}: ABCDEFGHIJKLMNOPQRSTUVWXYZ", cancellationToken);
        }

        await client.WriteAtAsync(0, 13, "----------------------------------------", Rift64Color.Cyan, cancellationToken);
        await client.WriteAtAsync(0, 14, "BOTTOM HALF (EXPECTED: LOWERCASE)", Rift64Color.White, cancellationToken);
        for (byte row = 16; row < 23; row++)
        {
            await client.SetCursorAsync(2, row, cancellationToken);
            await client.WriteTextAsync($"row {row:D2}: abcdefghijklmnopqrstuvwxyz", cancellationToken);
        }

        await client.WriteAtAsync(0, 24, "PRESS KEY TO ACTIVATE RASTER SPLIT.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);

        // Enable Raster Split (split line 154, which is exactly row 13/14).
        // Top: default uppercase charset pointer ($14). Bottom: lowercase ($16).
        var enableAck = await client.SetRasterSplitAsync(
            enable: true,
            splitLine: 154,
            topD011: 0x1B, topD016: 0x08, topD018: 0x14,
            botD011: 0x1B, botD016: 0x08, botD018: 0x16,
            cancellationToken);

        await client.WriteAtAsync(0, 24, $"SPLIT ACTIVE! ACK => {FormatAck(enableAck)}        ", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(0, 1, "NOTICE FONT CHANGES ABOVE/BELOW COLS. ", Rift64Color.Cyan, cancellationToken);
        await client.WriteAtAsync(0, 15, "PRESS KEY TO DISABLE SPLIT.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);

        var disableAck = await client.SetRasterSplitAsync(
            enable: false,
            splitLine: 154,
            topD011: 0x1B, topD016: 0x08, topD018: 0x14,
            botD011: 0x1B, botD016: 0x08, botD018: 0x16,
            cancellationToken);

        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 9: RASTER SPLIT DISABLED", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(0, 2, $"SPLIT DISABLED SUCCESSFULLY. ACK => {FormatAck(disableAck)}", Rift64Color.White, cancellationToken);
        await client.WriteAtAsync(0, 5, "PRESS ANY KEY TO RETURN TO MENU.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);
    }

    private static string FormatAck(bool? ack) => ack switch
    {
        true => "ACK",
        false => "NAK",
        null => "TIMEOUT"
    };
}
