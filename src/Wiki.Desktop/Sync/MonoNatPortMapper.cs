// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Threading;
using System.Threading.Tasks;
using Mono.Nat;

namespace Wiki.Desktop.Sync;

/// <summary>UPnP/NAT-PMP port mapping via Mono.Nat: discover an IGD, map the (TCP) port to the same external
/// number, and read the external IP. Returns null if no device answers within the timeout (CGNAT / no UPnP).</summary>
public sealed class MonoNatPortMapper : IPortMapper
{
    public async Task<PortMapping?> TryMapAsync(int internalPort, TimeSpan timeout, CancellationToken ct)
    {
        var found = new TaskCompletionSource<INatDevice>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFound(object? _, DeviceEventArgs e) => found.TrySetResult(e.Device);
        NatUtility.DeviceFound += OnFound;
        try
        {
            NatUtility.StartDiscovery();
            using var reg = ct.Register(() => found.TrySetCanceled(ct));
            var winner = await Task.WhenAny(found.Task, Task.Delay(timeout, ct));
            if (winner != found.Task) return null;

            var device = await found.Task;
            var granted = await device.CreatePortMapAsync(new Mapping(Protocol.Tcp, internalPort, internalPort));
            var ext = await device.GetExternalIPAsync();
            // The IGD may open a different external port than we asked for (ours already taken); advertise the
            // one it actually granted, not the one we requested — otherwise the invite points at a dead port.
            var externalPort = granted?.PublicPort > 0 ? granted.PublicPort : internalPort;
            return new PortMapping(ext.ToString(), externalPort);
        }
        catch { return null; }   // any UPnP failure → this rung simply yields nothing
        finally
        {
            NatUtility.DeviceFound -= OnFound;
            NatUtility.StopDiscovery();
        }
    }
}
