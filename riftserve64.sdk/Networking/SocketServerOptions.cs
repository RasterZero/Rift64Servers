using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace RiftServe64.Sdk.Networking;

public sealed class SocketServerOptions
{
    public const int DefaultPlainPort = 64080;
    public const int DefaultTlsPort = 64443;
    public const int DefaultMinimumBaudRate = 2400;
    public const int DefaultBaudRate = 38400;
    public const int DefaultMaxConnections = 4;

    public required IPAddress IpAddress { get; init; }
    public int Port { get; init; } = DefaultPlainPort;
    public int TlsPort { get; init; } = DefaultTlsPort;

    /// <summary>
    /// Selects which listeners are opened. Encryption is a server-to-server feature (chiefly for the
    /// RIFT GATE proxy reaching remote hosts over the internet); the Commodore 64 link itself is always
    /// unencrypted plain TCP. See <see cref="SocketEncryptionMode"/>.
    /// </summary>
    public SocketEncryptionMode EncryptionMode { get; init; } = SocketEncryptionMode.Unencrypted;

    /// <summary>
    /// Optional TLS server certificate used when <see cref="EncryptionMode"/> is
    /// <see cref="SocketEncryptionMode.Encrypted"/> or <see cref="SocketEncryptionMode.Both"/>.
    /// If <c>null</c>, a temporary 2048-bit self-signed certificate is generated in memory at startup.
    /// </summary>
    public X509Certificate2? ServerCertificate { get; init; }

    public int Backlog { get; init; } = 100;
    public int MaxConnections { get; init; } = DefaultMaxConnections;
    public int DefaultConnectionBaudRate { get; init; } = DefaultBaudRate;
    public int MinimumConnectionBaudRate { get; init; } = DefaultMinimumBaudRate;

    public static SocketServerOptions Create(
        string ipAddress,
        int port = DefaultPlainPort,
        int defaultConnectionBaudRate = DefaultBaudRate,
        int minimumConnectionBaudRate = DefaultMinimumBaudRate,
        int maxConnections = DefaultMaxConnections,
        int backlog = 100,
        int tlsPort = DefaultTlsPort,
        SocketEncryptionMode encryptionMode = SocketEncryptionMode.Unencrypted,
        X509Certificate2? serverCertificate = null)
    {
        if (!IPAddress.TryParse(ipAddress, out var parsedIpAddress))
        {
            throw new ArgumentException($"Invalid IP address: {ipAddress}", nameof(ipAddress));
        }

        var options = new SocketServerOptions
        {
            IpAddress = parsedIpAddress,
            Port = port,
            TlsPort = tlsPort,
            EncryptionMode = encryptionMode,
            ServerCertificate = serverCertificate,
            Backlog = backlog,
            MaxConnections = maxConnections,
            DefaultConnectionBaudRate = defaultConnectionBaudRate,
            MinimumConnectionBaudRate = minimumConnectionBaudRate
        };

        options.Validate();
        return options;
    }

    public void Validate()
    {
        if (Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), "Port must be between 1 and 65535.");
        }

        if (EncryptionMode is SocketEncryptionMode.Encrypted or SocketEncryptionMode.Both)
        {
            if (TlsPort is < 1 or > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(TlsPort), "TLS Port must be between 1 and 65535.");
            }
            if (Port == TlsPort && EncryptionMode is SocketEncryptionMode.Both)
            {
                throw new ArgumentException("Plain Port and TLS Port cannot be the same when dual-port mode (Both) is enabled.");
            }
        }

        if (Backlog <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Backlog), "Backlog must be greater than zero.");
        }

        if (MaxConnections <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConnections), "MaxConnections must be greater than zero.");
        }

        if (MinimumConnectionBaudRate < DefaultMinimumBaudRate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumConnectionBaudRate),
                $"Minimum baud rate cannot be lower than {DefaultMinimumBaudRate}.");
        }

        if (DefaultConnectionBaudRate < MinimumConnectionBaudRate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DefaultConnectionBaudRate),
                "Default connection baud rate cannot be lower than the configured minimum baud rate.");
        }
    }
}
