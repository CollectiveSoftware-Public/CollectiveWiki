// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using Wiki.Sync.Transport;

namespace Wiki.Sync.Transport.Tests;

/// <summary>An in-memory multicast group: every channel joined to the bus delivers each Send to all OTHER
/// members synchronously, tagged with the sender's fake source address — deterministic, no sockets/timers.</summary>
internal sealed class FakeMulticastBus
{
    private readonly List<FakeChannel> _members = new();

    public IMulticastChannel Join(string source)
    {
        var ch = new FakeChannel(this, IPAddress.Parse(source));
        _members.Add(ch);
        return ch;
    }

    private void Publish(FakeChannel from, byte[] data)
    {
        foreach (var m in _members.ToArray())
            if (!ReferenceEquals(m, from))
                m.Deliver(new MulticastDatagram(data, from.Source));
    }

    private sealed class FakeChannel(FakeMulticastBus bus, IPAddress source) : IMulticastChannel
    {
        public IPAddress Source { get; } = source;
        public event EventHandler<MulticastDatagram>? Received;
        public void Send(byte[] datagram) => bus.Publish(this, datagram);
        public void Deliver(MulticastDatagram d) => Received?.Invoke(this, d);
        public void Dispose() { }
    }
}
