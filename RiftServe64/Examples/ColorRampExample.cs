using RiftServe64.Sdk.Protocol;

/// <summary>
/// Demonstrates VIC-II Color RAM by painting blocks across all 16 hardware
/// colours using the reverse-video checkerboard glyph.
/// <para>
/// In standard text mode every screen cell at $0400 has a matching 4-bit colour
/// nybble at $D800-$DBE7, so the same character can appear in any of the 16
/// fixed VIC-II colours without touching the character ROM. This example uses
/// the <c>Q</c> (colour block fill) and <c>V</c> (coloured window) commands to
/// show how foreground colour is decoupled from glyph shape.
/// </para>
/// </summary>
public sealed class ColorRampExample : IRift64MenuExample
{
    public char Key => '2';
    public string MenuLabel => "Color Blocks";

    public async Task RunAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        var checker20 = new string(Rift64ProtocolClient.PetsciiCheckerboard, 20);
        var checker16 = new string(Rift64ProtocolClient.PetsciiCheckerboard, 16);

        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 2: Q + V COLOR OPS", Rift64Color.LightGreen, cancellationToken);

        // Use V to paint each row and write readable labels with contrasting text.
        for (byte color = 0; color < 16; color++)
        {
            var line = (byte)(2 + color);

            // Draw a checkerboard color band on the right side of the screen
            await client.SetCursorAsync(20, line, cancellationToken);
            await client.DrawColoredWindowAsync((Rift64Color)color, 20, 1, checker20, cancellationToken);

            // Write the high-contrast text label on the left side (over the black background)
            var labelColor = (color == (byte)Rift64Color.Black) ? Rift64Color.White : (Rift64Color)color;
            await client.SetCursorAsync(1, line, cancellationToken);
            await client.WriteColoredTextAsync(labelColor, $"COLOR {color:X1} BAND", cancellationToken);
        }

        await client.WriteAtAsync(1, 19, "MULTICOLORS", Rift64Color.White, cancellationToken);

        // Write checkerboards first so the color blocks are actually visible in standard text mode.
        await client.SetCursorAsync(20, 19, cancellationToken);
        await client.DrawColoredWindowAsync(Rift64Color.White, 16, 1, checker16, cancellationToken);

        // Use Q to paint a compact swatch strip (pure color-RAM write path).
        for (byte color = 0; color < 16; color++)
        {
            await client.FillColorBlockAsync((byte)(20 + color), 19, 1, 1, (Rift64Color)color, cancellationToken);
        }

        await client.SetCursorAsync(7, 20, cancellationToken);
        await client.DrawColoredWindowAsync(
            Rift64Color.LightBlue,
            26,
            2,
            "V COMMAND WRITES A COLORED TEXT WINDOW IN ONE CALL.",
            cancellationToken);

        await client.WriteAtAsync(0, 23, "PRESS ANY KEY TO RETURN TO MENU.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);
    }
}
