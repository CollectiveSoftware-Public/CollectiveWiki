// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using System.Net.Sockets;

namespace Wiki.Sync.Transport.Tests;

/// <summary>A connected pair of TCP streams on the loopback interface — real sockets, so SslStream runs a
/// genuine handshake, without spawning a second OS process.</summary>
internal static class Loopback
{
    public static (Stream Server, Stream Client) TcpPair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var accept = listener.AcceptTcpClientAsync();
        var client = new TcpClient();
        client.Connect((IPEndPoint)listener.LocalEndpoint);
        var server = accept.GetAwaiter().GetResult();
        listener.Stop();
        return (server.GetStream(), client.GetStream());
    }
}
