// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net.Sockets;
using Wiki.Sync;

namespace Wiki.Sync.Transport;

/// <summary>
/// Bounds a TLS accept loop against pre-authentication resource exhaustion: at most
/// <c>maxConcurrentHandshakes</c> handshakes run at once, and each must complete within
/// <c>handshakeTimeout</c> (a slowloris guard). An optional <c>serveTimeout</c> additionally bounds the
/// post-handshake exchange, so a peer that completes TLS but then stalls cannot hold its slot forever — used
/// by the pairing path, which admits ANY device (the invite token, not the roster, is the admission gate), so
/// an unauthenticated internet peer could otherwise complete the cheap handshake and idle-hold a slot. The
/// roster-gated sync path leaves it unset: only trusted peers reach its serve, and its transfers are unbounded
/// in length. Shared by <see cref="PairingListener"/> and <see cref="SyncListener"/> — the two networked accept
/// loops in this repo. (CollectiveRelay keeps its own inline copy by design: it is a dependency-free standalone
/// repo. The SSH agent's endpoint is local and owner-ACL'd, so it does not need connection caps — so this stays
/// a repo-local helper, not a shared Collective.Platform primitive.)
/// </summary>
internal sealed class ConnectionGate(TimeSpan handshakeTimeout, int maxConcurrentHandshakes, TimeSpan? serveTimeout = null)
{
    private int _inFlight;

    /// <summary>Handle one accepted connection: drop it when over the concurrent-handshake cap, else complete
    /// the mutual-TLS handshake within the timeout (admitting peers <paramref name="acceptPeer"/> approves)
    /// and hand the authenticated connection to <paramref name="serve"/>. When a <c>serveTimeout</c> is
    /// configured the serve runs under a linked deadline so an authenticated-but-idle peer is dropped rather
    /// than holding its slot. Failures are swallowed — the socket is already closed.</summary>
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
                using (conn)
                {
                    if (serveTimeout is { } timeout)
                    {
                        // Bound the post-handshake exchange: a peer that authenticated but then stalls the
                        // (single-round) exchange is cancelled after the deadline, freeing its slot. Linked to
                        // the caller ct so listener shutdown still cancels promptly.
                        using var serveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        serveCts.CancelAfter(timeout);
                        await serve(conn, serveCts.Token);
                    }
                    else
                    {
                        // No serve deadline (roster-gated sync): the post-handshake transfer uses the caller's
                        // ct, not the (already-elapsed) handshake timeout, so long legitimate syncs are unbounded.
                        await serve(conn, ct);
                    }
                }
            }
        }
        catch { /* handshake rejected/timed out, serve deadline elapsed, or peer dropped — connection closed */ }
        finally { Interlocked.Decrement(ref _inFlight); }
    }
}
