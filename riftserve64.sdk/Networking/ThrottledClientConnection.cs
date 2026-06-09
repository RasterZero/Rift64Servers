using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RiftServe64.Sdk.Networking;

public sealed class ThrottledClientConnection : IClientConnection
{
    private readonly TcpClient _client;
    private readonly Stream _stream;
    private readonly BaudRateThrottle _throttle;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _isDisposed;

    internal ThrottledClientConnection(
        TcpClient client,
        Stream stream,
        int initialBaudRate,
        int minimumBaudRate)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _throttle = new BaudRateThrottle(initialBaudRate, minimumBaudRate);
    }

    public Guid ConnectionId { get; } = Guid.NewGuid();

    public EndPoint? RemoteEndPoint => _client.Client.RemoteEndPoint;

    public int BaudRate => _throttle.BaudRate;

    public bool IsConnected => _client.Connected;

    public bool IsDataAvailable => _client.Connected && _client.Available > 0;

    internal event Action<ThrottledClientConnection>? Disposed;

    public void SetBaudRate(int baudRate)
    {
        ThrowIfDisposed();
        _throttle.SetBaudRate(baudRate);
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _throttle.WaitForTurnAsync(buffer.Length, cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _client.Dispose();
            _writeGate.Dispose();
            Disposed?.Invoke(this);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _isDisposed) == 1)
        {
            throw new ObjectDisposedException(nameof(ThrottledClientConnection));
        }
    }
}
