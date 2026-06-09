using RiftServe64.Sdk.Protocol;

/// <summary>
/// Demonstrates the RIFT64 capability handshake by re-querying the client at
/// runtime and comparing it to the identity captured at connect time.
/// <para>
/// Because the thin C64 client and the host server evolve independently, the
/// protocol carries a capability bitfield (queried with the <c>?</c> command)
/// that advertises which features the firmware supports. This example shows how
/// a host can re-validate that contract mid-session before invoking optional
/// commands, rather than assuming a fixed feature set.
/// </para>
/// </summary>
public sealed class CapabilityRecheckExample : IRift64MenuExample
{
    public char Key => '4';
    public string MenuLabel => "Capability Chk";

    public async Task RunAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 4: CAPABILITY RE-CHECK", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(0, 2, $"Init: {initialIdentity.CapabilitiesRaw}", Rift64Color.White, cancellationToken);

        var current = await client.QueryCapabilitiesAsync(cancellationToken);
        await client.WriteAtAsync(0, 4, $"Caps: {current}", Rift64Color.White, cancellationToken);

        // Actually exercise the wrappers we advertise.
        await client.WriteAtAsync(0, 7, "Live demo of SDK wrappers:", Rift64Color.Cyan, cancellationToken);

        // B - border
        await client.SetCursorAsync(0, 9, cancellationToken);
        await client.DrawBorderAsync(40, 6, Rift64BorderGlyphs.Default, cancellationToken);

        // V - colored window inside the border
        await client.SetCursorAsync(2, 10, cancellationToken);
        await client.DrawColoredWindowAsync(Rift64Color.LightGreen, 36, 1,
            "V: COLORED WINDOW PAYLOAD", cancellationToken);

        // L - length-prefixed text
        await client.SetCursorAsync(2, 11, cancellationToken);
        await client.WriteLengthTextAsync("L: LENGTH-PREFIXED TEXT WORKS.", cancellationToken);

        // Q - color block
        await client.FillColorBlockAsync(2, 12, 36, 1, Rift64Color.Yellow, cancellationToken);
        await client.SetCursorAsync(2, 12, cancellationToken);
        await client.WriteTextAsync("Q: BLOCK PAINTED YELLOW", cancellationToken);

        // E - erase line tail
        await client.SetCursorAsync(2, 13, cancellationToken);
        await client.WriteTextAsync("E: TAIL ERASES HERE -> XXXXXXXXXXX", cancellationToken);
        await client.SetCursorAsync(25, 13, cancellationToken);
        await client.EraseLineAsync(11, cancellationToken);

        await client.WriteAtAsync(0, 17, "Press any key to return to menu.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);
    }
}
