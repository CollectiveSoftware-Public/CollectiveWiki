// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using System.Net.Sockets;
using Wiki.Sync;

namespace Wiki.Sync.Transport.Tests;

public class PairingListenerTests
{
    [Fact]
    public async Task Admits_any_device_and_hands_the_authenticated_connection_to_on_peer()
    {
        using var owner = DeviceIdentity.Create();
        using var stranger = DeviceIdentity.Create();   // NOT on any roster — pairing still admits it
        var seen = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var listener = new PairingListener(owner, (conn, ct) =>
        {
            seen.TrySetResult(conn.RemoteDeviceId);
            return Task.CompletedTask;
        });
        var ep = listener.Start(new IPEndPoint(IPAddress.Loopback, 0));

        using var client = new TcpClient();
        await client.ConnectAsync(ep);
        using var conn = await TlsPeerConnection.AuthenticateClientAsync(client.GetStream(), stranger, owner.DeviceId);

        Assert.Equal(stranger.DeviceId, await seen.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task A_client_that_stalls_the_tls_handshake_is_dropped_after_the_timeout()
    {
        using var owner = DeviceIdentity.Create();
        using var listener = new PairingListener(owner, (_, _) => Task.CompletedTask,
            handshakeTimeout: TimeSpan.FromMilliseconds(200));
        var ep = listener.Start(new IPEndPoint(IPAddress.Loopback, 0));

        using var client = new TcpClient();
        await client.ConnectAsync(ep);
        // Never begin the TLS handshake (slowloris). The server must close the connection after the timeout.
        var buf = new byte[1];
        int n = await client.GetStream().ReadAsync(buf).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, n); // 0 bytes = server closed the connection (EOF)
    }
}
