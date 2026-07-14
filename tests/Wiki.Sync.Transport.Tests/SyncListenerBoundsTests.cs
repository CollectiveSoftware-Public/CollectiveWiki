// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using System.Net.Sockets;
using Wiki.Sync;
using Wiki.Sync.Transport;
using Xunit;

namespace Wiki.Sync.Transport.Tests;

public class SyncListenerBoundsTests
{
    private sealed class EmptyProvider : ISyncContentProvider
    {
        public IReadOnlyList<SignedFileEntry> Index() => [];
        public string? Content(string path) => null;
    }

    [Fact]
    public async Task A_client_that_stalls_the_tls_handshake_is_dropped_after_the_timeout()
    {
        using var owner = DeviceIdentity.Create();
        using var listener = new SyncListener(owner, new SyncServer(new EmptyProvider()), _ => true,
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
