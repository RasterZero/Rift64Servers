using RiftServe64.Sdk.Protocol;

/// <summary>
/// Demonstrates VIC-II display-register control and framed protocol packets.
/// <para>
/// The VIC-II display is configured by three registers: $D011 (control 1:
/// rows, bitmap/text, raster), $D016 (control 2: columns, multicolour), and
/// $D018 (memory pointers: which 1KB screen slot and which 2KB charset the VIC
/// reads, within the 16KB bank selected by CIA-2 port A). This example sets
/// border/background colour and charset via the <c>I</c>/<c>F</c> commands, and
/// shows the <c>~</c> framed-packet wrapper (note: a frame length of 0 means 256
/// bytes on the client, so a 1-byte dummy payload is sent for empty frames).
/// </para>
/// </summary>
public sealed class VicDisplayExample : IRift64MenuExample
{
    public char Key => '8';
    public string MenuLabel => "VIC & Framed";

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

    // Phase 2.3: best-effort restoration of the standard text display so the host
    // menu renders correctly even if this demo faulted or was cancelled mid-way.
    private static async Task RestoreClientStateAsync(Rift64ProtocolClient client)
    {
        try
        {
            await client.SetDisplayModeAsync(
                Rift64DisplayMode.Text,
                vicBank: VicBank.Bank0,
                d018: 0x14, d011: 0x1B, d016: 0x08,
                border: Rift64Color.Black, background: Rift64Color.Black,
                CancellationToken.None);
        }
        catch
        {
            // The connection may already be gone during shutdown; ignore.
        }
    }

    private async Task RunCoreAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        // The C64 firmware boots into VIC bank 0 with the screen at $0400 and
        // the uppercase character ROM shadow at $1000. The classic text-mode
        // register pair is therefore d018=$14, d011=$1B, d016=$08.
        var textLayout = VicLayout.ForText(VicBank.Bank0, VicScreenSlot.Slot1, VicCharsetSlot.Slot2);
        byte d018 = textLayout.D018;
        byte d011 = 0x1B;
        byte d016 = 0x08;

        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 8: VIC & FRAMED OPS", Rift64Color.LightGreen, cancellationToken);

        // 1. Demonstrate Framed Commands (~ command)
        await client.WriteAtAsync(0, 2, "1. SENDING FRAMED CLEAR (~ C)...", Rift64Color.White, cancellationToken);
        await client.WriteAtAsync(0, 4, "PRESS ANY KEY TO TRIGGER CLEAR.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);

        // Note: The C64 client firmware interprets a frame length of 0 as 256 bytes,
        // so we must send a 1-byte dummy payload to prevent the client from blocking.
        var clearAck = await client.SendFrameAsync((byte)'C', new byte[] { 0 }, cancellationToken);

        var textBytes = System.Text.Encoding.ASCII.GetBytes("HELLO! THIS IS S-R-F-M-D-T-X TEXT.");
        await client.SendFrameAsync((byte)'L', textBytes, cancellationToken);

        await client.WriteAtAsync(0, 3, $"FRAMED CLEAR ACK => {FormatAck(clearAck)}", Rift64Color.Cyan, cancellationToken);
        await client.WriteAtAsync(0, 5, "2. DEMONSTRATING DISPLAY MODES (I)...", Rift64Color.White, cancellationToken);
        await client.WriteAtAsync(0, 6, "PRESS ANY KEY TO TURN BORDER/BG CYAN.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);

        var modeAck = await client.SetDisplayModeAsync(
            Rift64DisplayMode.Text,
            vicBank: textLayout.Bank,
            d018: d018, d011: d011, d016: d016,
            border: Rift64Color.Cyan, background: Rift64Color.Cyan,
            cancellationToken);

        await client.WriteAtAsync(0, 8, $"DISPLAY MODE ACK => {FormatAck(modeAck)}", Rift64Color.White, cancellationToken);
        await client.WriteAtAsync(0, 10, "SCREEN BACKGROUND AND BORDER ARE CYAN!", Rift64Color.Yellow, cancellationToken);
        await client.WriteAtAsync(0, 12, "PRESS ANY KEY TO RESTORE TO BLACK.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);

        await client.SetDisplayModeAsync(
            Rift64DisplayMode.Text,
            vicBank: textLayout.Bank,
            d018: d018, d011: d011, d016: d016,
            border: Rift64Color.Black, background: Rift64Color.Black,
            cancellationToken);

        await client.WriteAtAsync(0, 14, "3. DEMONSTRATING CHARSET SETUP (F)...", Rift64Color.White, cancellationToken);
        var charsetAck = await client.SetCharsetBankAsync(vicBank: textLayout.Bank, d018: d018, cancellationToken);
        await client.WriteAtAsync(0, 16, $"CHARSET BANK ACK => {FormatAck(charsetAck)}", Rift64Color.Cyan, cancellationToken);

        await client.WriteAtAsync(0, 19, "PRESS ANY KEY TO RETURN.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);
    }

    private static string FormatAck(bool? ack) => ack switch
    {
        true => "ACK",
        false => "NAK",
        null => "TIMEOUT"
    };
}
