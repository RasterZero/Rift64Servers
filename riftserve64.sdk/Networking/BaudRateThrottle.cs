using System.Diagnostics;

namespace RiftServe64.Sdk.Networking;

internal sealed class BaudRateThrottle
{
    private const int BitsPerByteOnWire = 10;

    private readonly object _sync = new();
    private long _nextAvailableTick;
    private int _baudRate;

    public BaudRateThrottle(int initialBaudRate, int minimumBaudRate)
    {
        if (minimumBaudRate < SocketServerOptions.DefaultMinimumBaudRate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumBaudRate),
                $"Minimum baud rate cannot be lower than {SocketServerOptions.DefaultMinimumBaudRate}.");
        }

        MinimumBaudRate = minimumBaudRate;
        SetBaudRate(initialBaudRate);
    }

    public int MinimumBaudRate { get; }

    public int BaudRate => Volatile.Read(ref _baudRate);

    public void SetBaudRate(int baudRate)
    {
        if (baudRate < MinimumBaudRate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baudRate),
                $"Baud rate cannot be lower than {MinimumBaudRate}.");
        }

        Volatile.Write(ref _baudRate, baudRate);
    }

    public ValueTask WaitForTurnAsync(int payloadLengthBytes, CancellationToken cancellationToken)
    {
        if (payloadLengthBytes <= 0)
        {
            return ValueTask.CompletedTask;
        }

        long delayTicks;
        lock (_sync)
        {
            var nowTicks = Stopwatch.GetTimestamp();
            var transmissionTicks = CalculateTransmissionTicks(payloadLengthBytes, BaudRate);
            var scheduledStartTick = Math.Max(nowTicks, _nextAvailableTick);

            _nextAvailableTick = scheduledStartTick + transmissionTicks;
            delayTicks = scheduledStartTick - nowTicks;
        }

        if (delayTicks <= 0)
        {
            return ValueTask.CompletedTask;
        }

        var delayMs = (int)Math.Ceiling(delayTicks * 1000d / Stopwatch.Frequency);
        return new ValueTask(Task.Delay(delayMs, cancellationToken));
    }

    private static long CalculateTransmissionTicks(int payloadLengthBytes, int baudRate)
    {
        var bitsToSend = payloadLengthBytes * (double)BitsPerByteOnWire;
        var secondsToSend = bitsToSend / baudRate;
        return (long)Math.Ceiling(secondsToSend * Stopwatch.Frequency);
    }
}