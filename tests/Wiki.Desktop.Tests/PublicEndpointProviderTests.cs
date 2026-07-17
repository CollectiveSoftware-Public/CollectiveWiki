// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Wiki.Desktop.Sync;
using Xunit;

namespace Wiki.Desktop.Tests;

public class PublicEndpointProviderTests
{
    private sealed class FakeMapper(PortMapping? result) : IPortMapper
    {
        public Task<PortMapping?> TryMapAsync(int internalPort, TimeSpan timeout, CancellationToken ct)
            => Task.FromResult(result);
    }

    private sealed class TwoPortMapper : IPortMapper
    {
        public Task<PortMapping?> TryMapAsync(int internalPort, TimeSpan timeout, CancellationToken ct)
            // pretend the router maps each internal port to the same external number on 203.0.113.9
            => Task.FromResult<PortMapping?>(new PortMapping("203.0.113.9", internalPort));
    }

    private static PublicEndpointProvider Provider(PortMapping? map, IEnumerable<IPAddress> v6)
        => new(new FakeMapper(map), () => v6, () => "192.168.1.5");

    [Fact]
    public async Task Disabled_gathers_only_the_lan_candidate()
    {
        var p = Provider(new PortMapping("203.0.113.9", 40000), new[] { IPAddress.Parse("2001:db8::1") });
        var cands = await p.GatherAsync(8768, 8767, internetEnabled: false, CancellationToken.None);
        var only = Assert.Single(cands);
        Assert.Equal(new SyncCandidate("192.168.1.5", 8768, 8767), only);
    }

    [Fact]
    public async Task Enabled_gathers_lan_then_global_ipv6_then_upnp_ipv4()
    {
        var p = new PublicEndpointProvider(new TwoPortMapper(),
            () => new[] { IPAddress.Parse("fe80::1"), IPAddress.Parse("2001:db8::1") },
            () => "192.168.1.5");
        var cands = await p.GatherAsync(8768, 8767, internetEnabled: true, CancellationToken.None);
        Assert.Equal(new[]
        {
            new SyncCandidate("192.168.1.5", 8768, 8767),
            new SyncCandidate("2001:db8::1", 8768, 8767),
            new SyncCandidate("203.0.113.9", 8768, 8767),
        }, cands);
    }

    [Fact]
    public async Task Enabled_without_upnp_or_ipv6_still_yields_the_lan_candidate()
    {
        var p = Provider(map: null, v6: Array.Empty<IPAddress>());
        var cands = await p.GatherAsync(8768, 8767, internetEnabled: true, CancellationToken.None);
        Assert.Equal(new SyncCandidate("192.168.1.5", 8768, 8767), Assert.Single(cands));
    }
}
