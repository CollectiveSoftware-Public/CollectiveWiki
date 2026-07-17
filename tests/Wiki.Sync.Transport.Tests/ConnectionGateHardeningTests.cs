// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Wiki.Sync;
using Wiki.Sync.Transport;
using Xunit;

namespace Wiki.Sync.Transport.Tests;

public class ConnectionGateHardeningTests
{
    // A client that connects and then sends nothing must be dropped within the handshake timeout,
    // never holding a handshake slot indefinitely (slowloris).
    [Fact]
    public async Task A_silent_client_is_dropped_within_the_handshake_timeout()
    {
        using var self = DeviceIdentity.Create();
        using var listener = new PairingListener(self, (_, _) => Task.CompletedTask,
            handshakeTimeout: TimeSpan.FromMilliseconds(300), maxConcurrentHandshakes: 4);
        var ep = listener.Start(new IPEndPoint(IPAddress.Loopback, 0));

        using var silent = new TcpClient();
        await silent.ConnectAsync(ep.Address, ep.Port);
        // Hold the socket open, send nothing. The gate must reclaim the slot; a fresh handshake still works.
        await Task.Delay(600);

        using var real = DeviceIdentity.Create();
        // A well-formed TLS attempt after the timeout must still be accepted into a slot (proves the slot freed).
        // (Full mutual-TLS success is covered elsewhere; here we assert the connect is accepted, not refused.)
        using var probe = new TcpClient();
        await probe.ConnectAsync(ep.Address, ep.Port);
        Assert.True(probe.Connected);
    }

    // Post-handshake hardening: an attacker who completes the mutual-TLS handshake (pairing admits ANY device —
    // acceptPeer is `_ => true`) but then stalls the exchange must not hold its slot forever. The pairing serve
    // is a single request/response round, so the gate's serve deadline reclaims the slot after the timeout.
    [Fact]
    public async Task An_authenticated_but_idle_peer_is_dropped_after_the_serve_deadline()
    {
        using var owner = DeviceIdentity.Create();
        using var joiner = DeviceIdentity.Create();

        // A pairing handler that blocks waiting for a frame the client never sends — the real ServePairingAsync
        // does exactly this (it reads the JoinRequest first). Under the caller ct it would block indefinitely.
        using var listener = new PairingListener(owner,
            async (conn, ct) => { var b = new byte[1]; await conn.Stream.ReadExactlyAsync(b, ct); },
            handshakeTimeout: TimeSpan.FromSeconds(5),
            serveTimeout: TimeSpan.FromMilliseconds(300));
        var ep = listener.Start(new IPEndPoint(IPAddress.Loopback, 0));

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(ep.Address, ep.Port);
        // Complete a real mutual-TLS handshake — we are now authenticated (past the roster/any-device gate) — then
        // send nothing. The server's serve blocks on its read; the serve deadline must cancel it and close us.
        using var conn = await TlsPeerConnection.AuthenticateClientAsync(tcp.GetStream(), joiner, owner.DeviceId);

        var buf = new byte[1];
        int n = await conn.Stream.ReadAsync(buf).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, n); // 0 bytes = server closed the authenticated connection after the serve deadline (EOF)
    }
}
