// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using System.Net.Sockets;

namespace Wiki.Sync.Transport;

/// <summary>The real mDNS transport: a UDP socket joined to the standard mDNS multicast group
/// (224.0.0.251:5353) that raises <see cref="Received"/> for every datagram and sends to the group. Thin BCL
/// wrapper; the discovery logic (responder/browser) is exercised headlessly over a fake channel because
/// multicast is unreliable on CI/headless hosts.</summary>
public sealed class UdpMulticastChannel : IMulticastChannel
{
    private readonly UdpClient _udp;
    private readonly IPEndPoint _group;
    private readonly CancellationTokenSource _cts = new();

    public event EventHandler<MulticastDatagram>? Received;

    public UdpMulticastChannel()
    {
        _group = new IPEndPoint(ServiceConstants.MulticastAddress, ServiceConstants.MulticastPort);
        _udp = new UdpClient(AddressFamily.InterNetwork);
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, ServiceConstants.MulticastPort));
        _udp.JoinMulticastGroup(ServiceConstants.MulticastAddress);
        _ = ReceiveLoopAsync(_cts.Token);
    }

    public void Send(byte[] datagram) => _udp.Send(datagram, datagram.Length, _group);

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try { result = await _udp.ReceiveAsync(ct); }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }
            Received?.Invoke(this, new MulticastDatagram(result.Buffer, result.RemoteEndPoint.Address));
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _udp.Dispose();
        _cts.Dispose();
    }
}
