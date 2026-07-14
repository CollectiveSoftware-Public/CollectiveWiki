// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net.Sockets;
using Wiki.Sync;

namespace Wiki.Sync.Transport;

/// <summary>
/// Bounds a TLS accept loop against pre-authentication resource exhaustion: at most
/// <c>maxConcurrentHandshakes</c> handshakes run at once, and each must complete within
/// <c>handshakeTimeout</c> (a slowloris guard). Shared by <see cref="PairingListener"/> and
/// <see cref="SyncListener"/> — the two networked accept loops in this repo. (CollectiveRelay keeps its own
/// inline copy by design: it is a dependency-free standalone repo. The SSH agent's endpoint is local and
/// owner-ACL'd, so it does not need connection caps — so this stays a repo-local helper, not a shared
/// Collective.Platform primitive.)
/// </summary>
internal sealed class ConnectionGate(TimeSpan handshakeTimeout, int maxConcurrentHandshakes)
{
    private int _inFlight;

    /// <summary>Handle one accepted connection: drop it when over the concurrent-handshake cap, else complete
    /// the mutual-TLS handshake within the timeout (admitting peers <paramref name="acceptPeer"/> approves)
    /// and hand the authenticated connection to <paramref name="serve"/>. Failures are swallowed — the socket
    /// is already closed.</summary>
    public async Task HandleAsync(TcpClient client, DeviceIdentity self, Func<string, bool> acceptPeer,
        Func<TlsPeerConnection, CancellationToken, Task> serve, CancellationToken ct)
    {
        if (Interlocked.Increment(ref _inFlight) > maxConcurrentHandshakes)
        {
            Interlocked.Decrement(ref _inFlight);
            client.Dispose(); // over the concurrent-handshake cap — drop
            return;
        }
        try
        {
            using (client)
            {
                TlsPeerConnection conn;
                // Bound the TLS handshake: a client that connects but stalls the handshake is dropped.
                using (var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    handshakeCts.CancelAfter(handshakeTimeout);
                    conn = await TlsPeerConnection.AuthenticateServerAsync(client.GetStream(), self, acceptPeer, handshakeCts.Token);
                }
                // The post-handshake exchange uses the caller's ct, not the (already-elapsed) handshake timeout.
                using (conn)
                    await serve(conn, ct);
            }
        }
        catch { /* handshake rejected/timed out or peer dropped — connection closed, nothing to do */ }
        finally { Interlocked.Decrement(ref _inFlight); }
    }
}
