using RiftServe64.Sdk.Protocol;

/// <summary>
/// Demonstrates upstream telemetry: the client streaming joystick and sprite
/// collision state back to the host in real time.
/// <para>
/// The two CIA control-port registers ($DC00/$DC01) report joystick directions
/// and fire, while the VIC-II latches sprite-to-sprite and sprite-to-background
/// collisions in $D01E/$D01F. The <c>J</c> command opens a telemetry session
/// that pushes framed samples (rate-divided by <c>divider</c>) which the host
/// decodes via <see cref="Rift64TelemetryFrame"/>. Note that C64 control port 1
/// shares the keyboard matrix, so phantom fire signals are filtered and only a
/// literal 'Q' ends the session.
/// </para>
/// </summary>
public sealed class TelemetryDemoExample : IRift64MenuExample
{
    public char Key => 'B';
    public string MenuLabel => "Telemetry";

    public async Task RunAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 11: UPSTREAM TELEMETRY (J)", Rift64Color.LightGreen, cancellationToken);

        await client.WriteAtAsync(0, 2, "MOVE JOYSTICKS OR CAUSE COLLISIONS.", Rift64Color.White, cancellationToken);
        await client.WriteAtAsync(0, 4, "PRESS 'Q' ON C64 TO EXIT.", Rift64Color.Yellow, cancellationToken);

        await client.WriteAtAsync(0, 7, "SEQ  JOY1            JOY2            SPR", Rift64Color.Cyan, cancellationToken);
        await client.WriteAtAsync(0, 8, "----------------------------------------", Rift64Color.Cyan, cancellationToken);

        Rift64TelemetryFrame? lastFrame = null;
        bool exitTriggered = false;

        await using var telemetry = await client.StartTelemetrySessionAsync(
            divider: 3,
            TelemetryChannels.Joy1 | TelemetryChannels.Joy2 | TelemetryChannels.SpriteToSprite,
            cancellationToken);

        telemetry.FrameReceived += f => lastFrame = f;
        telemetry.UnsolicitedBytes += b =>
        {
            // C64 Port 1 shares the keyboard matrix and produces phantom signals
            // on joystick fire; only treat literal 'Q'/'q' as a quit request.
            if (b is (byte)'Q' or (byte)'q')
            {
                exitTriggered = true;
            }
        };

        while (!cancellationToken.IsCancellationRequested && !exitTriggered)
        {
            await telemetry.PumpAsync(cancellationToken);

            if (lastFrame is { } frame)
            {
                lastFrame = null;

                var joy1Str = frame.GetJoy1Directions().PadRight(15);
                var joy2Str = frame.GetJoy2Directions().PadRight(15);

                await client.WriteAtAsync(0, 10, $"{frame.Seq:D3}  {joy1Str} {joy2Str} ${frame.SprSpr:X2}", Rift64Color.White, cancellationToken);
            }
        }

        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 11: COMPLETE", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(0, 2, "TELEMETRY DEMO COMPLETED.", Rift64Color.White, cancellationToken);
        await client.WriteAtAsync(0, 4, "PRESS ANY KEY TO RETURN.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);
    }
}
