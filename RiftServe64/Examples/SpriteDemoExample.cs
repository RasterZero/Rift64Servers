using RiftServe64.Sdk.Protocol;

/// <summary>
/// Demonstrates VIC-II hardware sprites: upload, enable, colour, multicolour,
/// expansion, and batched position updates.
/// <para>
/// The VIC-II has 8 independent 24x21 sprites whose shapes are 64-byte blocks
/// selected by a 1-byte pointer stored at the end of the screen matrix
/// ($07F8-$07FF in bank 0). A pointer value <c>p</c> resolves the pattern to
/// vicBankBase + p*64, so pointer 13 in bank 0 maps to $0340. Sprites can be
/// hi-res or multicolour and X/Y-expanded. The <c>@</c> command updates all
/// sprite coordinates in one batched packet — the key to smooth motion over a
/// slow serial link.
/// </para>
/// </summary>
public sealed class SpriteDemoExample : IRift64MenuExample
{
    public char Key => '7';
    public string MenuLabel => "Sprite Ops";

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

    // Phase 2.3: best-effort restoration that disables every sprite so the host
    // menu is clean even if this demo faulted or was cancelled mid-animation.
    private static async Task RestoreClientStateAsync(Rift64ProtocolClient client)
    {
        try
        {
            for (byte i = 0; i < 8; i++)
            {
                await client.SetSpriteAsync(i, 0, 0, Rift64Color.Black, 0, bank: VicBank.Bank0, enabled: false, CancellationToken.None);
            }
        }
        catch
        {
            // The connection may already be gone during shutdown; ignore.
        }
    }

    private async Task RunCoreAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 7: VIC-II SPRITE DEMO", Rift64Color.LightGreen, cancellationToken);

        // The RIFT64 firmware runs the VIC in bank 0 ($0000-$3FFF) with
        // screen RAM at $0400 and the char ROM shadow at $1000. Sprite pointer
        // 13 in bank 0 resolves to $0000 + 13 * 64 = $0340. The typed VicBank
        // overload below converts to CIA port-A bits internally.
        const VicBank spriteBank = VicBank.Bank0;
        const byte spritePointer = 13;

        // 1. Upload custom sprite pattern to $0340 (bank 0, pointer 13).
        await client.WriteAtAsync(0, 2, "1. UPLOADING SPRITE PATTERN TO $0340...", Rift64Color.White, cancellationToken);
        var spritePattern = new byte[64];
        Array.Fill(spritePattern, (byte)0x55); // Checkerboard pattern for sprite
        await client.UploadSpriteAsync(spriteBank, spritePointer, spritePattern, cancellationToken);

        // 2. Set Sprite 0 (Y command) - monochrome hires
        await client.WriteAtAsync(0, 4, "2. ENABLING SPRITE 0 (HI-RES)...", Rift64Color.White, cancellationToken);
        await client.SetSpriteAsync(0, 100, 100, Rift64Color.Yellow, spritePointer, bank: spriteBank, enabled: true, cancellationToken);

        // 3. Set Sprite 1 (U command) - multicolor & expanded
        await client.WriteAtAsync(0, 6, "3. ENABLING SPRITE 1 (MULTICOLOR)...", Rift64Color.White, cancellationToken);
        await client.SetSpriteMulticolorAsync(
            spriteId: 1,
            x: 150,
            y: 100,
            color: Rift64Color.Cyan,
            pointer: spritePointer,
            bank: spriteBank,
            enabled: true,
            multicolor: true,
            expandX: true,
            expandY: true,
            priority: false,
            sharedColor0: Rift64Color.Red,
            sharedColor1: Rift64Color.White,
            cancellationToken: cancellationToken);

        await client.WriteAtAsync(0, 10, "SPRITES 0 & 1 ARE NOW ON THE SCREEN.", Rift64Color.LightBlue, cancellationToken);
        await client.WriteAtAsync(0, 12, "PRESS ANY KEY TO ANIMATE POSITIONS.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);

        // 4. Batch position changes using (@ command)
        await client.WriteAtAsync(0, 14, "4. ANIMATING SPRITES VIA @ BATCH...", Rift64Color.White, cancellationToken);
        for (int i = 0; i < 20; i++)
        {
            var positions = new Dictionary<byte, (int X, byte Y)>
            {
                [0] = (100 + i * 4, (byte)(100 + i * 2)),
                [1] = (150 - i * 4, (byte)(100 + i * 2))
            };
            await client.SetSpritePositionsAsync(positions, cancellationToken);
            await Task.Delay(100, cancellationToken);
        }

        await client.WriteAtAsync(0, 16, "ANIMATION DONE. PRESS KEY TO CLEAN UP.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);

        // Disable both sprites
        await client.SetSpriteAsync(0, 0, 0, Rift64Color.Black, 0, bank: spriteBank, enabled: false, cancellationToken: cancellationToken);
        await client.SetSpriteMulticolorAsync(1, 0, 0, Rift64Color.Black, 0, bank: spriteBank, enabled: false, cancellationToken: cancellationToken);

        await client.WriteAtAsync(0, 18, "SPRITES CLEANED UP.", Rift64Color.White, cancellationToken);
        await client.WriteAtAsync(0, 20, "PRESS ANY KEY TO RETURN.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);
    }
}
