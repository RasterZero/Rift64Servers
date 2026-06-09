using RiftServe64.Sdk.Networking;
using RiftServe64.Sdk.Protocol;

const int plainPort = 8002;
const int tlsPort = 64443;
const int maxConnections = 4;

// Dual-port hosting. RIFT GATE (and other server-side clients) can reach this host either over plain
// TCP or over TLS. The Commodore 64 itself always connects unencrypted via its local RIFT GATE proxy;
// TLS here secures the server-to-server WAN hop only.
//
// Passing serverCertificate: null makes the SDK generate a temporary self-signed certificate in memory
// at startup. Supply a real X509Certificate2 for production deployments.
var options = SocketServerOptions.Create(
	ipAddress: "0.0.0.0",
	port: plainPort,
	maxConnections: maxConnections,
	tlsPort: tlsPort,
	encryptionMode: SocketEncryptionMode.Both,
	serverCertificate: null);

var server = SocketServer.GetOrCreate(options);

server.ClientConnected += connection =>
{
	_ = Task.Run(async () =>
	{
		try
		{
			var client = new Rift64ProtocolClient(connection);
			var host = new Rift64InteractiveMenuHost(client);
			await host.RunAsync().ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Client session error: {ex.Message}");
		}
		finally
		{
			await connection.DisposeAsync().ConfigureAwait(false);
		}
	});

	return ValueTask.CompletedTask;
};

await server.StartAsync();
Console.WriteLine($"RiftServe64 SDK server (dual-port) listening on 0.0.0.0");
Console.WriteLine($"  Plain TCP : {plainPort}");
Console.WriteLine($"  TLS       : {tlsPort}");
Console.WriteLine($"Maximum concurrent client connections: {maxConnections}");
Console.WriteLine("Connect from VICE/C64 client. Press Ctrl+C to stop.");

try
{
	await Task.Delay(Timeout.Infinite);
}
finally
{
	await server.StopAsync();
}
