// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;
using Wiki.Sync.Transport;

namespace Wiki.Sync.Transport.Tests;

public class MdnsDiscoveryTests
{
    [Fact]
    public void A_browser_discovers_a_responder_on_the_bus()
    {
        using var id = DeviceIdentity.Create();
        var bus = new FakeMulticastBus();
        var responderChannel = bus.Join("192.168.1.50");
        var browserChannel = bus.Join("192.168.1.51");

        using var responder = new MdnsResponder(id.DeviceId, 55321, responderChannel);
        using var browser = new MdnsBrowser(browserChannel);

        DiscoveredPeer? found = null;
        browser.PeerDiscovered += (_, p) => found = p;
        browser.Browse();

        Assert.NotNull(found);
        Assert.Equal(id.DeviceId, found!.DeviceId);
        Assert.Equal(55321, found.Port);
        Assert.Equal("192.168.1.50", found.Host);
    }

    [Fact]
    public void An_unsolicited_announce_is_also_discovered()
    {
        using var id = DeviceIdentity.Create();
        var bus = new FakeMulticastBus();
        var responderChannel = bus.Join("10.0.0.7");
        var browserChannel = bus.Join("10.0.0.8");

        using var browser = new MdnsBrowser(browserChannel);
        using var responder = new MdnsResponder(id.DeviceId, 6000, responderChannel);

        DiscoveredPeer? found = null;
        browser.PeerDiscovered += (_, p) => found = p;
        responder.Announce();

        Assert.NotNull(found);
        Assert.Equal(id.DeviceId, found!.DeviceId);
        Assert.Equal(6000, found.Port);
    }
}
