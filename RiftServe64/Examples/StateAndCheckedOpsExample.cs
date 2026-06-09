using RiftServe64.Sdk.Protocol;

/// <summary>
/// Demonstrates screen state save/restore and checksum-verified memory writes.
/// <para>
/// The firmware keeps two off-screen save slots so a host can snapshot the
/// 1000-byte screen matrix (<c>S</c>) and later restore it (<c>R</c>) — the same
/// technique a pause menu uses. It also contrasts the unchecked store (<c>M</c>)
/// against the checked store (<c>Z</c>), where the client returns an ACK/NAK
/// derived from a rolling checksum, and the checked window (<c>X</c>). Writes
/// here target the safe $C000 upload zone, never the client program at
/// $0801-$37FF.
/// </para>
/// </summary>
public sealed class StateAndCheckedOpsExample : IRift64MenuExample
{
    public char Key => '5';
    public string MenuLabel => "State & Ops";

    public async Task RunAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 5: S/R/M/Z/X", Rift64Color.LightGreen, cancellationToken);

        await client.WriteAtAsync(0, 2, "S: SAVING CURRENT SCREEN TO BUFFER 0...", Rift64Color.White, cancellationToken);
        await client.SaveScreenBufferAsync(0, cancellationToken);
        await Task.Delay(2000, cancellationToken);

        await client.SetCursorAsync(0, 4, cancellationToken);
        await client.DrawWindowAsync(38, 2, "TEMPORARY CONTENT BEFORE RESTORE OPERATION.", cancellationToken);
        await Task.Delay(2000, cancellationToken);

        await client.WriteAtAsync(0, 7, "R: RESTORING BUFFER 0...", Rift64Color.White, cancellationToken);
        await Task.Delay(1000, cancellationToken);

        await client.RestoreScreenBufferAsync(0, cancellationToken);
        await Task.Delay(500, cancellationToken);

        await client.WriteAtAsync(0, 7, "R: RESTORED BUFFER 0 SUCCESSFULLY.", Rift64Color.LightGreen, cancellationToken);
        await Task.Delay(1500, cancellationToken);

        // Unchecked memory store to a harmless RAM region.
        var uncheckedPayload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await client.StoreMemoryAsync(0xC000, uncheckedPayload, cancellationToken);
        await client.WriteAtAsync(0, 9, "M: WROTE 8 BYTES TO $C000 (UNCHECKED).", Rift64Color.Cyan, cancellationToken);

        var checkedPayload = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 };
        var checkedMemoryAck = await client.StoreMemoryCheckedAsync(0xC010, checkedPayload, cancellationToken);
        await client.WriteAtAsync(0, 11, $"Z: checked write to $C010 => {FormatAck(checkedMemoryAck)}", Rift64Color.White, cancellationToken);

        await client.SetCursorAsync(0, 14, cancellationToken);
        var checkedWindowAck = await client.DrawWindowCheckedAsync(
            34,
            2,
            "X CMD: CHECKSUMMED WINDOW PAYLOAD",
            cancellationToken);

        await client.WriteAtAsync(0, 17, $"X: checked window => {FormatAck(checkedWindowAck)}", Rift64Color.White, cancellationToken);
        await client.WriteAtAsync(0, 20, "Press any key to return to menu.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);
    }

    private static string FormatAck(bool? ack) => ack switch
    {
        true => "ACK",
        false => "NAK",
        null => "TIMEOUT"
    };
}