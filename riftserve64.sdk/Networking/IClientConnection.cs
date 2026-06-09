using System.Net;

namespace RiftServe64.Sdk.Networking;

public interface IClientConnection : IAsyncDisposable
{
    Guid ConnectionId { get; }
    EndPoint? RemoteEndPoint { get; }
    int BaudRate { get; }
    bool IsConnected { get; }
    bool IsDataAvailable { get; }

    void SetBaudRate(int baudRate);
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
}