// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using System.Net.Sockets;
using Wiki.Sync;

namespace Wiki.Sync.Transport;

/// <summary>Accepts TCP connections on a local endpoint, completes a mutual-TLS handshake admitting only
/// peers <paramref name="acceptPeer"/> approves, and serves each with a <see cref="SyncServer"/>. Loopback/LAN
/// direct connect (spec §8 tier 1); the relay tier layers in at Plan E. Start returns the actual bound
/// endpoint (bind port 0 → OS-assigned ephemeral port). The accept path is bounded by a shared
/// <see cref="ConnectionGate"/> (handshake timeout + concurrent-handshake cap) so a stalled handshake cannot
/// hold the listener before the roster check runs.</summary>
public sealed class SyncListener : IDisposable
{
    private static readonly TimeSpan DefaultHandshakeTimeout = TimeSpan.FromSeconds(10);

    private readonly DeviceIdentity _self;
    private readonly SyncServer _server;
    private readonly Func<string, bool> _acceptPeer;
    private readonly ConnectionGate _gate;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public SyncListener(DeviceIdentity self, SyncServer server, Func<string, bool> acceptPeer,
        TimeSpan? handshakeTimeout = null, int maxConcurrentHandshakes = 64)
    {
        _self = self;
        _server = server;
        _acceptPeer = acceptPeer;
        _gate = new ConnectionGate(handshakeTimeout ?? DefaultHandshakeTimeout, maxConcurrentHandshakes);
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
            _ = _gate.HandleAsync(client, _self, _acceptPeer, (conn, c) => _server.ServeAsync(conn.Stream, c), ct);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _cts?.Dispose();
    }
}
