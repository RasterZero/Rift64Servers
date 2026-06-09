using RiftServe64.Sdk.Protocol;

/// <summary>
/// Demonstrates hardware cursor visibility and region scrolling.
/// <para>
/// The C64 KERNAL maintains a blinking cursor whose visibility is toggled via
/// the <c>H</c> command. Region scrolling (<c>G</c>) shifts a rectangular block
/// of the $0400 screen matrix up/down/left/right in a single firmware operation
/// — far cheaper over the serial link than rewriting every affected cell. This
/// example contrasts the two and is the basis for the scrolling used by the
/// metatile viewport demo.
/// </para>
/// </summary>
public sealed class CursorAndScrollExample : IRift64MenuExample
{
    public char Key => '6';
    public string MenuLabel => "Cursor & Scroll";

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

    // Phase 2.3: best-effort restoration that re-enables the text cursor so the
    // host menu is not left with a hidden cursor after a fault or cancellation.
    private static async Task RestoreClientStateAsync(Rift64ProtocolClient client)
    {
        try
        {
            await client.SetCursorVisibilityAsync(true, CancellationToken.None);
        }
        catch
        {
            // The connection may already be gone during shutdown; ignore.
        }
    }

    private async Task RunCoreAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 6: CURSOR & SCROLL (H/G)", Rift64Color.LightGreen, cancellationToken);

        // 1. Demonstrate Cursor Visibility (H command)
        await client.WriteAtAsync(0, 2, "DEMONSTRATING CURSOR VISIBILITY...", Rift64Color.White, cancellationToken);
        await client.WriteAtAsync(0, 4, "PRESS ANY KEY TO HIDE CURSOR & SCROLL.", Rift64Color.Yellow, cancellationToken);

        // Position the cursor exactly at column 38 on row 4 (1 space after the word "SCROLL.")
        await client.SetCursorAsync(38, 4, cancellationToken);
        await client.SetCursorVisibilityAsync(true, cancellationToken);

        await client.PauseForKeyAsync(cancellationToken: cancellationToken);

        await client.SetCursorVisibilityAsync(false, cancellationToken);

        // 2. Demonstrate Scrolling (G command)
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 6: REGION SCROLLING (G)", Rift64Color.LightGreen, cancellationToken);

        byte scrollX = 10;
        byte scrollY = 4;
        byte scrollW = 20;
        byte scrollH = 10;

        await client.SetCursorAsync(scrollX, scrollY, cancellationToken);
        await client.DrawBorderAsync((byte)(scrollW + 2), (byte)(scrollH + 2), Rift64BorderGlyphs.Default, cancellationToken);

        for (byte row = 0; row < scrollH; row++)
        {
            await client.SetCursorAsync((byte)(scrollX + 1), (byte)(scrollY + 1 + row), cancellationToken);
            await client.WriteTextAsync($"ROW-{row} ABCDEFGHIJK", cancellationToken);
        }

        await client.WriteAtAsync(0, 17, "PRESS KEY TO SCROLL UP.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);

        await client.ScrollRegionAsync((byte)(scrollX + 1), (byte)(scrollY + 1), scrollW, scrollH, Rift64ScrollDirection.Up, cancellationToken);

        await client.WriteAtAsync(0, 17, "SCROLLED UP! PRESS KEY TO SCROLL DOWN.  ", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);

        await client.ScrollRegionAsync((byte)(scrollX + 1), (byte)(scrollY + 1), scrollW, scrollH, Rift64ScrollDirection.Down, cancellationToken);

        await client.WriteAtAsync(0, 17, "SCROLLED DOWN! PRESS KEY TO RETURN.     ", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);
    }
}
