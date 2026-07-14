// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Wiki.Desktop.Sync;

/// <summary>Best-effort pick of this machine's LAN IPv4 address to advertise in an invite. Falls back to
/// loopback when no up, non-loopback IPv4 interface is found (the user can still supply a host override).</summary>
public static class LanEndpoints
{
    public static string LocalIPv4()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                    return ua.Address.ToString();
        }
        return IPAddress.Loopback.ToString();
    }
}
