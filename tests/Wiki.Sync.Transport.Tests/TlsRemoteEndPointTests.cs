// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using System.Net.Sockets;
using Wiki.Sync;

namespace Wiki.Sync.Transport.Tests;

public class TlsRemoteEndPointTests
{
    [Fact]
    public async Task Both_ends_capture_the_peer_socket_endpoint()
    {
        using var server = DeviceIdentity.Create();
        using var client = DeviceIdentity.Create();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var ep = (IPEndPoint)listener.LocalEndpoint;

        var acceptTask = listener.AcceptTcpClientAsync();
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(ep);
        using var accepted = await acceptTask;

        var serverConnTask = TlsPeerConnection.AuthenticateServerAsync(accepted.GetStream(), server, _ => true);
        using var clientConn = await TlsPeerConnection.AuthenticateClientAsync(tcp.GetStream(), client, server.DeviceId);
        using var serverConn = await serverConnTask;

        Assert.NotNull(serverConn.RemoteEndPoint);
        Assert.NotNull(clientConn.RemoteEndPoint);
        Assert.Equal(ep.Port, clientConn.RemoteEndPoint!.Port);   // client's remote endpoint is the listener
        listener.Stop();
    }
}
