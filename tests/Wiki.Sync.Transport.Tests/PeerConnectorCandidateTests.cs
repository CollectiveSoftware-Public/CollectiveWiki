// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Wiki.Sync;
using Wiki.Sync.Transport;
using Xunit;

namespace Wiki.Sync.Transport.Tests;

public class PeerConnectorCandidateTests
{
    private static AuthorizedPeer Entry(DeviceIdentity id, PeerRole role) => new(id.DeviceId, id.PublicKey, role, "n", "e");

    private static int ClosedPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public async Task Connects_via_the_first_reachable_candidate_after_a_dead_one()
    {
        using var owner = DeviceIdentity.Create();
        using var joiner = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Entry(owner, PeerRole.Owner), Entry(joiner, PeerRole.ReadWrite) });
        var server = new SyncServer(new ReplicaContentProvider(new VaultReplica(owner.DeviceId), new ChangeSigner(owner)));
        using var lan = new SyncListener(owner, server, id => peers.Find(id) is not null);
        var live = lan.Start(new IPEndPoint(IPAddress.Loopback, 0));

        var candidates = new List<IPEndPoint>
        {
            new(IPAddress.Loopback, ClosedPort()),   // dead — refused fast
            live,                                     // reachable
        };
        using var conn = await PeerConnector.ConnectAsync(joiner, owner.DeviceId, candidates,
            perCandidateTimeout: TimeSpan.FromSeconds(2));
        Assert.Equal(owner.DeviceId, conn.RemoteDeviceId);
    }

    [Fact]
    public async Task Throws_invalid_operation_when_the_candidate_list_is_empty()
        => await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PeerConnector.ConnectAsync(DeviceIdentity.Create(), "someid", Array.Empty<IPEndPoint>()));

    [Fact]
    public async Task Throws_when_every_candidate_fails()
    {
        using var joiner = DeviceIdentity.Create();
        var candidates = new List<IPEndPoint> { new(IPAddress.Loopback, ClosedPort()) };
        await Assert.ThrowsAnyAsync<Exception>(() =>
            PeerConnector.ConnectAsync(joiner, "someid", candidates, perCandidateTimeout: TimeSpan.FromSeconds(2)));
    }
}
