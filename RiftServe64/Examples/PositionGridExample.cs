using RiftServe64.Sdk.Protocol;

/// <summary>
/// Demonstrates absolute cursor positioning and PETSCII frame drawing on the
/// 40x25 character grid.
/// <para>
/// The C64 text screen is a fixed 40-column by 25-row matrix; any cell is
/// addressed as $0400 + row*40 + col. This example uses the <c>W</c> (window)
/// and <c>B</c> (border) commands to draw nested boxes from the PETSCII
/// line-drawing glyphs, showing how UI chrome is composed entirely from
/// character-ROM shapes rather than pixels.
/// </para>
/// </summary>
public sealed class PositionGridExample : IRift64MenuExample
{
    public char Key => '3';
    public string MenuLabel => "Window & Border";

    public async Task RunAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        await client.ClearScreenAsync(cancellationToken);

        await client.SetCursorAsync(1, 1, cancellationToken);
        await client.DrawBorderAsync(38, 22, Rift64BorderGlyphs.Default, cancellationToken);

        await client.SetCursorAsync(3, 3, cancellationToken);
        await client.DrawWindowAsync(
            34,
            8,
            "WINDOW TEXT USES FIXED SIZE PAYLOADS. SDK PADS OR TRUNCATES TO FIT WIDTH BY HEIGHT CLEANLY.",
            cancellationToken);

        await client.WriteAtAsync(3, 13, "LEGACY CURSOR OPS WORK PERFECTLY:", Rift64Color.White, cancellationToken);

        for (byte row = 0; row < 3; row++)
        {
            for (byte col = 0; col < 3; col++)
            {
                var x = (byte)(4 + col * 10);
                var y = (byte)(15 + row * 2);
                await client.SetCursorAsync(x, y, cancellationToken);
                await client.WriteTextAsync($"({x:D2},{y:D2})", cancellationToken);
            }
        }

        await client.WriteAtAsync(0, 24, "Press any key to return to menu.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);
    }
}
