using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace RiftServe64.Sdk.Networking;

public sealed class SocketServer : IAsyncDisposable
{
    private static readonly object SingletonLock = new();
    private static SocketServer? _instance;

    private readonly SocketServerOptions _options;
    private readonly TcpListener? _plainListener;
    private readonly TcpListener? _tlsListener;
    private readonly ConcurrentDictionary<Guid, ThrottledClientConnection> _connections = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly X509Certificate2? _certificate;

    private CancellationTokenSource? _acceptLoopCancellationSource;
    private Task? _acceptLoopTask;
    private int _started;
    private int _disposed;

    private SocketServer(SocketServerOptions options)
    {
        _options = options;

        if (_options.EncryptionMode is SocketEncryptionMode.Unencrypted or SocketEncryptionMode.Both)
        {
            _plainListener = new TcpListener(_options.IpAddress, _options.Port);
        }

        if (_options.EncryptionMode is SocketEncryptionMode.Encrypted or SocketEncryptionMode.Both)
        {
            _tlsListener = new TcpListener(_options.IpAddress, _options.TlsPort);

            _certificate = _options.ServerCertificate;
            if (_certificate is null)
            {
                Console.WriteLine("[SocketServer] No SSL/TLS certificate provided. Generating temporary self-signed certificate in memory...");
                _certificate = GenerateSelfSignedCertificate();
                Console.WriteLine($"[SocketServer] Temporary certificate generated successfully: {_certificate.Subject}");
            }
        }
    }

    public static SocketServer Instance =>
        _instance ?? throw new InvalidOperationException("SocketServer has not been initialized.");

    public event Func<ThrottledClientConnection, ValueTask>? ClientConnected;

    public event Func<ThrottledClientConnection, ValueTask>? ClientDisconnected;

    public IReadOnlyCollection<ThrottledClientConnection> Connections => _connections.Values.ToArray();

    public static SocketServer GetOrCreate(SocketServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        lock (SingletonLock)
        {
            if (_instance is not null)
            {
                if (_instance._options.Port != options.Port ||
                    _instance._options.TlsPort != options.TlsPort ||
                    _instance._options.EncryptionMode != options.EncryptionMode ||
                    !_instance._options.IpAddress.Equals(options.IpAddress))
                {
                    throw new InvalidOperationException(
                        $"SocketServer already initialized on {_instance._options.IpAddress}:{_instance._options.Port}. " +
                        $"Dispose the existing instance before creating one with different options.");
                }

                return _instance;
            }

            _instance = new SocketServer(options);
            return _instance;
        }
    }

    public static SocketServer GetOrCreate(
        string ipAddress,
        int port = SocketServerOptions.DefaultPlainPort,
        int defaultConnectionBaudRate = SocketServerOptions.DefaultBaudRate,
        int minimumConnectionBaudRate = SocketServerOptions.DefaultMinimumBaudRate,
        int maxConnections = SocketServerOptions.DefaultMaxConnections,
        int backlog = 100,
        int tlsPort = SocketServerOptions.DefaultTlsPort,
        SocketEncryptionMode encryptionMode = SocketEncryptionMode.Unencrypted,
        X509Certificate2? serverCertificate = null)
    {
        var options = SocketServerOptions.Create(
            ipAddress,
            port,
            defaultConnectionBaudRate,
            minimumConnectionBaudRate,
            maxConnections,
            backlog,
            tlsPort,
            encryptionMode,
            serverCertificate);

        return GetOrCreate(options);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _started) == 1)
            {
                return;
            }

            _acceptLoopCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var acceptTasks = new List<Task>();

            if (_plainListener is not null)
            {
                _plainListener.Start(_options.Backlog);
                acceptTasks.Add(AcceptLoopAsync(_plainListener, isTls: false, _acceptLoopCancellationSource.Token));
                Console.WriteLine($"[SocketServer] Plain TCP listener active on {_options.IpAddress}:{_options.Port}");
            }

            if (_tlsListener is not null)
            {
                _tlsListener.Start(_options.Backlog);
                acceptTasks.Add(AcceptLoopAsync(_tlsListener, isTls: true, _acceptLoopCancellationSource.Token));
                Console.WriteLine($"[SocketServer] TLS Encrypted listener active on {_options.IpAddress}:{_options.TlsPort}");
            }

            _acceptLoopTask = Task.WhenAll(acceptTasks);
            Volatile.Write(ref _started, 1);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _started) == 0)
            {
                return;
            }

            Volatile.Write(ref _started, 0);
            _acceptLoopCancellationSource?.Cancel();

            _plainListener?.Stop();
            _tlsListener?.Stop();

            if (_acceptLoopTask is not null)
            {
                try
                {
                    await _acceptLoopTask.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Expected task cancellation exceptions
                }
            }

            _acceptLoopCancellationSource?.Dispose();
            _acceptLoopCancellationSource = null;
            _acceptLoopTask = null;
        }
        finally
        {
            _lifecycleGate.Release();
        }

        var shutdownTasks = _connections.Values.Select(static connection => connection.DisposeAsync().AsTask());
        await Task.WhenAll(shutdownTasks).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Dispose();

            lock (SingletonLock)
            {
                if (ReferenceEquals(_instance, this))
                {
                    _instance = null;
                }
            }
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, bool isTls, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var tcpClient = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);

                if (_connections.Count >= _options.MaxConnections)
                {
                    tcpClient.Dispose();
                    continue;
                }

                // Handle the connection and perform Ssl Handshake in background to prevent blocking
                _ = Task.Run(async () =>
                {
                    try
                    {
                        Stream stream;
                        if (isTls)
                        {
                            if (_certificate is null)
                            {
                                tcpClient.Dispose();
                                return;
                            }

                            var sslStream = new SslStream(tcpClient.GetStream(), leaveInnerStreamOpen: false);
                            await sslStream.AuthenticateAsServerAsync(_certificate).ConfigureAwait(false);
                            stream = sslStream;
                        }
                        else
                        {
                            stream = tcpClient.GetStream();
                        }

                        var connection = new ThrottledClientConnection(
                            tcpClient,
                            stream,
                            _options.DefaultConnectionBaudRate,
                            _options.MinimumConnectionBaudRate);

                        connection.Disposed += HandleConnectionDisposed;
                        _connections.TryAdd(connection.ConnectionId, connection);

                        if (ClientConnected is not null)
                        {
                            await ClientConnected(connection).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SocketServer] Error accepting client connection: {ex.Message}");
                        tcpClient.Dispose();
                    }
                }, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
        catch (ObjectDisposedException)
        {
            // Expected during shutdown.
        }
        catch (SocketException) when (Volatile.Read(ref _started) == 0)
        {
            // Expected listener exception when stopped.
        }
    }

    private void HandleConnectionDisposed(ThrottledClientConnection connection)
    {
        connection.Disposed -= HandleConnectionDisposed;

        if (!_connections.TryRemove(connection.ConnectionId, out _))
        {
            return;
        }

        if (ClientDisconnected is null)
        {
            return;
        }

        _ = Task.Run(async () => await ClientDisconnected(connection).ConfigureAwait(false));
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            throw new ObjectDisposedException(nameof(SocketServer));
        }
    }

    private static X509Certificate2 GenerateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=RiftServe64SelfSigned",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // Server Auth
                critical: true));

        var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        // Export and re-import is a bulletproof way across platforms to get a valid cert with associated private key.
        return new X509Certificate2(certificate.Export(X509ContentType.Pfx), (string?)null);
    }
}
