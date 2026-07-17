// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Wiki.Sync.Transport;

namespace Wiki.Desktop.Sync;

/// <summary>Gathers this device's dialable candidates for an invite: LAN IPv4 always; and, when internet sync
/// is enabled, any global IPv6 address plus a UPnP/NAT-PMP-mapped public IPv4. The two ctor delegates make
/// the assembly logic testable without touching the real network.</summary>
public sealed class PublicEndpointProvider
{
    private readonly IPortMapper _mapper;
    private readonly Func<IEnumerable<IPAddress>> _globalV6;
    private readonly Func<string> _lanIPv4;

    public PublicEndpointProvider() : this(new MonoNatPortMapper(), DefaultGlobalV6, LanEndpoints.LocalIPv4) { }

    public PublicEndpointProvider(IPortMapper mapper, Func<IEnumerable<IPAddress>> globalV6, Func<string> lanIPv4)
    {
        _mapper = mapper;
        _globalV6 = globalV6;
        _lanIPv4 = lanIPv4;
    }

    /// <summary>The fast candidates that need no network round-trip: LAN IPv4 always, plus this device's global
    /// IPv6 addresses when internet sync is on. Both are read synchronously off local NIC state, so the caller
    /// can advertise them in the very first invite (before the slow UPnP mapping in <see cref="GatherAsync"/> has
    /// finished). No UPnP, no async.</summary>
    public IReadOnlyList<SyncCandidate> GatherLocal(int pairingPort, int syncPort, bool internetEnabled)
    {
        var list = new List<SyncCandidate> { new(_lanIPv4(), pairingPort, syncPort) };
        if (!internetEnabled) return list;

        foreach (var v6 in _globalV6().Where(AddressScope.IsGlobal))
            list.Add(new SyncCandidate(v6.ToString(), pairingPort, syncPort));

        return list;
    }

    public async Task<IReadOnlyList<SyncCandidate>> GatherAsync(int pairingPort, int syncPort, bool internetEnabled, CancellationToken ct)
    {
        var list = new List<SyncCandidate>(GatherLocal(pairingPort, syncPort, internetEnabled));
        if (!internetEnabled) return list;

        var timeout = TimeSpan.FromSeconds(5);
        var pair = await _mapper.TryMapAsync(pairingPort, timeout, ct);
        var sync = await _mapper.TryMapAsync(syncPort, timeout, ct);
        if (pair is not null && sync is not null && pair.ExternalIp == sync.ExternalIp)
            list.Add(new SyncCandidate(pair.ExternalIp, pair.ExternalPort, sync.ExternalPort));

        return list;
    }

    private static IEnumerable<IPAddress> DefaultGlobalV6()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                if (ua.Address.AddressFamily == AddressFamily.InterNetworkV6 && AddressScope.IsGlobal(ua.Address))
                    yield return ua.Address;
        }
    }
}
