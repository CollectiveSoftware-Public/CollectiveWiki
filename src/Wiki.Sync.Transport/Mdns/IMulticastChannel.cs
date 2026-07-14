// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;

namespace Wiki.Sync.Transport;

/// <summary>A datagram received on the multicast group: the raw bytes and the sender's address.</summary>
public sealed record MulticastDatagram(byte[] Data, IPAddress Source);

/// <summary>The multicast transport the mDNS responder/browser send and receive over. Abstracted so the
/// discovery logic tests against an in-memory bus (deterministic, no real sockets/timers); the real
/// implementation is <see cref="UdpMulticastChannel"/>.</summary>
public interface IMulticastChannel : IDisposable
{
    event EventHandler<MulticastDatagram>? Received;
    void Send(byte[] datagram);
}
