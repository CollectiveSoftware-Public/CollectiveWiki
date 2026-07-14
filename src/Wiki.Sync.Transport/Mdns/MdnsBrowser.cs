// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Sync.Transport;

/// <summary>Browses the LAN for CollectiveWiki peers: sends a PTR query for the service type and raises
/// <see cref="PeerDiscovered"/> for each response, reading the device id from TXT, the port from SRV, and the
/// host from the datagram source address. Event-driven so it tests deterministically over an in-memory bus;
/// on a real channel the caller collects discoveries over a listen window.</summary>
public sealed class MdnsBrowser : IDisposable
{
    private readonly IMulticastChannel _channel;

    public MdnsBrowser(IMulticastChannel channel)
    {
        _channel = channel;
        _channel.Received += OnReceived;
    }

    public event EventHandler<DiscoveredPeer>? PeerDiscovered;

    public void Browse()
    {
        var query = new DnsMessage { IsResponse = false };
        query.Questions.Add(new DnsQuestion(ServiceConstants.ServiceType, DnsType.PTR));
        _channel.Send(query.Encode());
    }

    private void OnReceived(object? sender, MulticastDatagram d)
    {
        DnsMessage msg;
        try { msg = DnsMessage.Decode(d.Data); }
        catch { return; }
        if (!msg.IsResponse) return;

        var srv = msg.Answers.OfType<SrvRecord>().FirstOrDefault();
        var txt = msg.Answers.OfType<TxtRecord>().FirstOrDefault();
        if (srv is null || txt is null) return;

        var prefix = ServiceConstants.DeviceIdTxtKey + "=";
        var idEntry = txt.Strings.FirstOrDefault(s => s.StartsWith(prefix, StringComparison.Ordinal));
        if (idEntry is null) return;

        PeerDiscovered?.Invoke(this, new DiscoveredPeer(idEntry[prefix.Length..], d.Source.ToString(), srv.Port));
    }

    public void Dispose() => _channel.Received -= OnReceived;
}
