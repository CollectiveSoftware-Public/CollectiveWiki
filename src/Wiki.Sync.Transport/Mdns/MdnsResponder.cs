// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Sync.Transport;

/// <summary>Announces this device's sync service on the LAN: answers browse queries for the CollectiveWiki
/// service type with PTR+SRV+TXT records carrying the device id and port, and can announce unsolicited.
/// TXT carries "id=&lt;deviceId&gt;"; SRV carries the port; the browser reads the datagram source for the host.</summary>
public sealed class MdnsResponder : IDisposable
{
    private readonly string _deviceId;
    private readonly int _port;
    private readonly string _instanceName;
    private readonly IMulticastChannel _channel;

    public MdnsResponder(string deviceId, int port, IMulticastChannel channel)
    {
        _deviceId = deviceId;
        _port = port;
        _instanceName = $"{deviceId}.{ServiceConstants.ServiceType}";
        _channel = channel;
        _channel.Received += OnReceived;
    }

    public void Announce() => _channel.Send(BuildResponse().Encode());

    private void OnReceived(object? sender, MulticastDatagram d)
    {
        DnsMessage msg;
        try { msg = DnsMessage.Decode(d.Data); }
        catch { return; }
        if (msg.IsResponse) return;
        bool asksForUs = msg.Questions.Any(q =>
            q.Type == DnsType.PTR &&
            string.Equals(q.Name, ServiceConstants.ServiceType, StringComparison.OrdinalIgnoreCase));
        if (asksForUs) _channel.Send(BuildResponse().Encode());
    }

    private DnsMessage BuildResponse()
    {
        var msg = new DnsMessage { IsResponse = true };
        msg.Answers.Add(new PtrRecord(ServiceConstants.ServiceType, 120, _instanceName));
        msg.Answers.Add(new SrvRecord(_instanceName, 120, (ushort)_port, $"{_deviceId}.local"));
        msg.Answers.Add(new TxtRecord(_instanceName, 120, new[] { $"{ServiceConstants.DeviceIdTxtKey}={_deviceId}" }));
        return msg;
    }

    public void Dispose() => _channel.Received -= OnReceived;
}
