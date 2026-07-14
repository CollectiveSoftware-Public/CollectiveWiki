// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using System.Net.Sockets;
using Wiki.Sync;
using Wiki.Sync.Transport;

namespace Wiki.Sync.Transport.Tests;

public class PeerConnectorTests
{
    private static AuthorizedPeer Entry(DeviceIdentity id, PeerRole role) => new(id.DeviceId, id.PublicKey, role, "n", "e");

    private static int ClosedPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port; // now closed → a connect to it is refused fast
    }

    [Fact]
    public async Task Prefers_the_direct_lan_endpoint_when_it_is_reachable()
    {
        using var owner = DeviceIdentity.Create();
        using var joiner = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Entry(owner, PeerRole.Owner), Entry(joiner, PeerRole.ReadWrite) });

        var server = new SyncServer(new ReplicaContentProvider(new VaultReplica(owner.DeviceId), new ChangeSigner(owner)));
        using var lan = new SyncListener(owner, server, id => peers.Find(id) is not null);
        var lanEp = lan.Start(new IPEndPoint(IPAddress.Loopback, 0));

        // A bogus relay endpoint that would fail if used — proving the LAN tier was taken.
        using var conn = await PeerConnector.ConnectAsync(joiner, owner.DeviceId, lanEp, new RelayEndpoint("127.0.0.1", 1));
        Assert.Equal(owner.DeviceId, conn.RemoteDeviceId);
    }

    [Fact]
    public async Task Falls_back_to_the_relay_when_the_lan_endpoint_is_dead()
    {
        using var owner = DeviceIdentity.Create();
        using var joiner = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Entry(owner, PeerRole.Owner), Entry(joiner, PeerRole.ReadWrite) });

        using var relay = new TestRelay();
        var relayEp = relay.Start();
        var server = new SyncServer(new ReplicaContentProvider(new VaultReplica(owner.DeviceId), new ChangeSigner(owner)));
        using var wan = new RelaySyncListener(owner, server, id => peers.Find(id) is not null);
        await wan.StartAsync(relayEp.Address.ToString(), relayEp.Port);
        await relay.WaitForRegistrationAsync(owner.DeviceId);

        var deadLan = new IPEndPoint(IPAddress.Loopback, ClosedPort()); // nothing listening → connect refused
        using var conn = await PeerConnector.ConnectAsync(
            joiner, owner.DeviceId, deadLan, new RelayEndpoint(relayEp.Address.ToString(), relayEp.Port));
        Assert.Equal(owner.DeviceId, conn.RemoteDeviceId);
    }
}
