// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Wiki.Sync;

namespace Wiki.Sync.Transport;

/// <summary>A mutually-authenticated TLS channel to one peer. Both ends present their device certificate;
/// each verifies the other by pinned device id (key fingerprint), ignoring PKI chain/name/expiry errors
/// (identity is pinned, not PKI-trusted — spec §6). The authenticated <see cref="Stream"/> carries the sync
/// protocol; <see cref="RemoteDeviceId"/> is the verified fingerprint of the peer that connected.</summary>
public sealed class TlsPeerConnection : IDisposable
{
    private TlsPeerConnection(SslStream stream, string remoteDeviceId, IPEndPoint? remoteEndPoint)
    {
        Stream = stream;
        RemoteDeviceId = remoteDeviceId;
        RemoteEndPoint = remoteEndPoint;
    }

    public SslStream Stream { get; }
    public string RemoteDeviceId { get; }

    /// <summary>The connected peer's socket endpoint, captured during the handshake (null if the transport is
    /// not a socket stream). Used by the head to learn a collaborator's return address for the pull direction.</summary>
    public IPEndPoint? RemoteEndPoint { get; }

    /// <summary>Server side: authenticate an accepted transport, requiring a client certificate and admitting
    /// only peers <paramref name="acceptPeer"/> approves (e.g. those on the authorized-peers list).</summary>
    public static async Task<TlsPeerConnection> AuthenticateServerAsync(
        Stream transport, DeviceIdentity self, Func<string, bool> acceptPeer, CancellationToken ct = default)
    {
        string? remoteId = null;
        var ssl = new SslStream(transport, leaveInnerStreamOpen: false);
        var options = new SslServerAuthenticationOptions
        {
            ServerCertificate = self.TlsCertificate,
            ClientCertificateRequired = true,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            RemoteCertificateValidationCallback = (_, cert, _, _) =>
            {
                if (cert is null) return false;
                remoteId = CertPinning.DeviceIdOf(cert);
                return acceptPeer(remoteId);
            },
        };
        try { await ssl.AuthenticateAsServerAsync(options, ct); }
        catch { await ssl.DisposeAsync(); throw; }
        var remote = (transport as NetworkStream)?.Socket.RemoteEndPoint as IPEndPoint;
        return new TlsPeerConnection(ssl, remoteId!, remote);
    }

    /// <summary>Client side: authenticate to a peer, pinning the expected server device id.</summary>
    public static async Task<TlsPeerConnection> AuthenticateClientAsync(
        Stream transport, DeviceIdentity self, string expectedServerDeviceId, CancellationToken ct = default)
    {
        var ssl = new SslStream(transport, leaveInnerStreamOpen: false);
        var options = new SslClientAuthenticationOptions
        {
            TargetHost = "collectivewiki",
            ClientCertificates = new X509CertificateCollection { self.TlsCertificate },
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            RemoteCertificateValidationCallback = (_, cert, _, _) =>
                cert is not null && CertPinning.DeviceIdOf(cert) == expectedServerDeviceId,
        };
        try { await ssl.AuthenticateAsClientAsync(options, ct); }
        catch { await ssl.DisposeAsync(); throw; }
        var remote = (transport as NetworkStream)?.Socket.RemoteEndPoint as IPEndPoint;
        return new TlsPeerConnection(ssl, expectedServerDeviceId, remote);
    }

    public void Dispose() => Stream.Dispose();
}
