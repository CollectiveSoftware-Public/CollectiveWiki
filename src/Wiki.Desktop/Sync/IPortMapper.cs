// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Wiki.Desktop.Sync;

/// <summary>A router port mapping: the external address + port a private port was mapped to.</summary>
public sealed record PortMapping(string ExternalIp, int ExternalPort);

/// <summary>Best-effort UPnP/NAT-PMP port mapping. Returns null when no cooperating router is found in time
/// (CGNAT, UPnP disabled, no IGD). Kept behind an interface so <see cref="PublicEndpointProvider"/> is unit-testable.</summary>
public interface IPortMapper
{
    Task<PortMapping?> TryMapAsync(int internalPort, TimeSpan timeout, CancellationToken ct);

    /// <summary>Remove a mapping previously granted by <see cref="TryMapAsync"/>, so the router stops forwarding
    /// WAN traffic to this device when the user opts back out of internet sync. Best-effort like the map: any
    /// failure (router gone, UPnP since disabled, mapping already expired) is swallowed — never throws.</summary>
    Task TryUnmapAsync(int internalPort, int externalPort, TimeSpan timeout, CancellationToken ct);
}
