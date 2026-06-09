namespace RiftServe64.Sdk.Protocol;

public sealed partial class Rift64ProtocolClient
{
    /// <summary>
    /// Sends a 'W' window command using <paramref name="screenCodes"/> exactly as
    /// supplied (no charset conversion). Use this when the caller has already
    /// encoded the cells for the active charset (e.g. the lowercase/uppercase
    /// charset selected via <c>SetCharsetBankAsync(..., 0x17)</c>).
    /// Length must equal <paramref name="width"/> * <paramref name="height"/>
    /// after width/height are clamped by the protocol.
    /// </summary>
    public Task DrawWindowRawAsync(
        byte width,
        byte height,
        ReadOnlyMemory<byte> screenCodes,
        CancellationToken cancellationToken = default)
    {
        var payload = BuildWindowPayload(width, height, screenCodes.Span);
        return SendCommandAsync(CommandWindow, payload, cancellationToken);
    }

    /// <summary>
    /// Sends a 'V' uniform-color window command using <paramref name="screenCodes"/>
    /// exactly as supplied (no charset conversion). See <see cref="DrawWindowRawAsync"/>.
    /// </summary>
    public Task DrawColoredWindowRawAsync(
        Rift64Color color,
        byte width,
        byte height,
        ReadOnlyMemory<byte> screenCodes,
        CancellationToken cancellationToken = default)
    {
        var content = BuildWindowPayload(width, height, screenCodes.Span);
        var payload = new byte[1 + content.Length];
        payload[0] = EncodeHexNibble((byte)color);
        content.CopyTo(payload, 1);
        return SendCommandAsync(CommandColoredWindow, payload, cancellationToken);
    }

    /// <summary>
    /// Reads bytes from the connection until the literal token <c>READY</c>
    /// followed by CR or LF is observed, or <paramref name="timeout"/> elapses.
    /// The C64 firmware emits this token unsolicited after boot; do not call
    /// <see cref="IdentifyClientAsync"/> first or you may race that emission.
    /// Returns <c>true</c> if READY was seen, <c>false</c> on timeout.
    /// Bytes received before READY are buffered for subsequent reads.
    /// </summary>
    public async Task<bool> WaitForReadyAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        var buffer = new byte[64];
        var matchPos = 0;

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            while (DateTime.UtcNow < deadline)
            {
                var read = await _connection.ReadAsync(buffer, timeoutSource.Token).ConfigureAwait(false);
                if (read <= 0)
                {
                    return false;
                }

                var match = "READY"u8;
                for (var i = 0; i < read; i++)
                {
                    var b = (byte)(buffer[i] & 0x7F);

                    if (matchPos < match.Length)
                    {
                        if (b == match[matchPos])
                        {
                            matchPos++;
                            continue;
                        }

                        // Mismatch — buffer the partial bytes we'd consumed plus this byte
                        // so callers don't lose pre-READY noise.
                        for (var j = 0; j < matchPos; j++)
                        {
                            _pendingBytes.Enqueue(match[j]);
                        }
                        matchPos = 0;
                        _pendingBytes.Enqueue(b);
                    }
                    else
                    {
                        // Awaiting CR/LF terminator after "READY"
                        if (b == (byte)'\r' || b == (byte)'\n')
                        {
                            // Re-queue any bytes after the terminator within this chunk
                            for (var k = i + 1; k < read; k++)
                            {
                                _pendingBytes.Enqueue((byte)(buffer[k] & 0x7F));
                            }
                            return true;
                        }

                        // Non-terminator after READY — treat as false match, buffer everything.
                        for (var j = 0; j < match.Length; j++)
                        {
                            _pendingBytes.Enqueue(match[j]);
                        }
                        matchPos = 0;
                        _pendingBytes.Enqueue(b);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        return false;
    }
}
