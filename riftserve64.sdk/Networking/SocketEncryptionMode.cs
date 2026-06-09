namespace RiftServe64.Sdk.Networking;

/// <summary>
/// Controls which transport listeners a <see cref="SocketServer"/> opens.
/// <para>
/// Encryption in RIFT64 is strictly a <b>server-to-server</b> concern, intended primarily for the
/// RIFT GATE proxy to reach remote RiftServe64 hosts securely across the public internet. The serial
/// link between a Commodore 64 client and its local RIFT GATE proxy is <b>always unencrypted plain TCP</b>:
/// the ~1&#160;MHz 6502 has no cryptographic hardware acceleration and cannot perform a TLS handshake or
/// AES stream decryption in real time. This is a permanent hardware constraint.
/// </para>
/// </summary>
public enum SocketEncryptionMode
{
    /// <summary>Only the unencrypted plain TCP port is active. (Default.)</summary>
    Unencrypted = 1,

    /// <summary>Only the TLS encrypted port is active; all traffic is encrypted.</summary>
    Encrypted = 2,

    /// <summary>Concurrent dual-port: both the unencrypted and TLS ports run side-by-side.</summary>
    Both = 3
}
