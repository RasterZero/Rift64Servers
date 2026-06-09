using RiftServe64.Sdk.Networking;
using RiftWriter;

var server = SocketServer.GetOrCreate("0.0.0.0", WriterConstants.Port, maxConnections: 1);

server.ClientConnected += connection =>
{
    _ = Task.Run(async () =>
    {
        try
        {
            var session = new WriterSession(connection);
            await session.RunAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Writer session error: {ex.Message}");
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    });

    return ValueTask.CompletedTask;
};

await server.StartAsync();
Console.WriteLine($"RiftWriter listening on {WriterConstants.Host}:{WriterConstants.Port}");
Console.WriteLine("Waiting for C64 client connection. Press Ctrl+C to stop.");

try
{
    await Task.Delay(Timeout.Infinite);
}
finally
{
    await server.StopAsync();
}
