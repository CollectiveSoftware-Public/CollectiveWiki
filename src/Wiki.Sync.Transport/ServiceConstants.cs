// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;

namespace Wiki.Sync.Transport;

/// <summary>Shared mDNS/DNS-SD identifiers for CollectiveWiki peer discovery on the LAN.</summary>
public static class ServiceConstants
{
    /// <summary>The DNS-SD service type peers browse for.</summary>
    public const string ServiceType = "_cwiki-sync._tcp.local";

    /// <summary>The TXT record key that carries a peer's device id.</summary>
    public const string DeviceIdTxtKey = "id";

    /// <summary>Standard mDNS multicast group + port (RFC 6762).</summary>
    public static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.251");
    public const int MulticastPort = 5353;
}
