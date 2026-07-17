// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using System.Net.Sockets;
using Wiki.Sync;

namespace Wiki.Sync.Transport;

/// <summary>Accepts TCP connections and completes a mutual-TLS handshake that admits ANY device
/// (<c>acceptPeer: _ =&gt; true</c>) — a joiner is not yet on the roster, so pairing is gated by the one-time
/// invite token at the application layer, not by roster membership — then hands each authenticated connection
/// to <paramref name="onPeer"/>. Run only while the owner is actively inviting. Start returns the bound
/// endpoint (bind port 0 → OS-assigned ephemeral port). The accept path is bounded by a shared
/// <see cref="ConnectionGate"/> (handshake timeout + concurrent-handshake cap, S3); the invite token remains
/// the actual admission gate.</summary>
public sealed class PairingListener : IDisposable
{
    private static readonly TimeSpan DefaultHandshakeTimeout = TimeSpan.FromSeconds(10);
    // Pairing admits ANY device (roster-independent), so an unauthenticated internet peer can complete the cheap
    // mutual-TLS handshake and then stall the exchange, holding its slot. The exchange is a single request/response
    // round the joiner sends immediately, so this generous deadline never trips a legitimate joiner but bounds an
    // idle attacker to one slot for at most this long (vs. indefinitely).
    private static readonly TimeSpan DefaultServeTimeout = TimeSpan.FromSeconds(30);

    private readonly DeviceIdentity _self;
    private readonly Func<TlsPeerConnection, CancellationToken, Task> _onPeer;
    private readonly ConnectionGate _gate;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public PairingListener(DeviceIdentity self, Func<TlsPeerConnection, CancellationToken, Task> onPeer,
        TimeSpan? handshakeTimeout = null, int maxConcurrentHandshakes = 64, TimeSpan? serveTimeout = null)
    {
        _self = self;
        _onPeer = onPeer;
        _gate = new ConnectionGate(handshakeTimeout ?? DefaultHandshakeTimeout, maxConcurrentHandshakes,
            serveTimeout ?? DefaultServeTimeout);
    }

    public IPEndPoint Start(IPEndPoint bind)
    {
        _listener = new TcpListener(bind);
        if (bind.AddressFamily == AddressFamily.InterNetworkV6)
            _listener.Server.DualMode = true;   // accept IPv4-mapped clients too
        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = AcceptLoopAsync(_listener, _cts.Token);
        return (IPEndPoint)_listener.LocalEndpoint;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { return; }
            // Admit ANY device (roster-independent); the invite token gates pairing at the app layer.
            _ = _gate.HandleAsync(client, _self, _ => true, _onPeer, ct);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _cts?.Dispose();
    }
}
