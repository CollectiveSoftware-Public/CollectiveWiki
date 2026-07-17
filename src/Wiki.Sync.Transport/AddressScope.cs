// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using System.Net.Sockets;

namespace Wiki.Sync.Transport;

/// <summary>Classifies an IP address as globally routable or not, so the head can distinguish an owner who
/// advertised no reachable address (LAN-only) from one whose addresses simply didn't connect.</summary>
public static class AddressScope
{
    public static bool IsGlobal(IPAddress addr)
    {
        if (IPAddress.IsLoopback(addr)) return false;
        if (addr.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = addr.GetAddressBytes();
            if (b[0] == 10) return false;                              // 10/8
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false; // 172.16/12
            if (b[0] == 192 && b[1] == 168) return false;             // 192.168/16
            if (b[0] == 169 && b[1] == 254) return false;             // 169.254/16 link-local
            return true;
        }
        if (addr.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // A dual-stack accept reports an IPv4 peer as ::ffff:a.b.c.d — classify it by the embedded IPv4,
            // else a mapped private address (::ffff:192.168.x.x) would slip through the v6 checks as "global".
            if (addr.IsIPv4MappedToIPv6) return IsGlobal(addr.MapToIPv4());
            if (addr.IsIPv6LinkLocal || addr.IsIPv6SiteLocal) return false;
            var b = addr.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return false;                  // fc00::/7 unique-local
            return true;
        }
        return false;
    }
}
