// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using System.Threading.Tasks;
using Wiki.Sync;
using Wiki.Sync.Transport;
using Xunit;

namespace Wiki.Sync.Transport.Tests;

public class DualStackListenerTests
{
    private static AuthorizedPeer Entry(DeviceIdentity id, PeerRole role) => new(id.DeviceId, id.PublicKey, role, "n", "e");

    [Fact]
    public async Task Sync_listener_bound_to_ipv6any_accepts_an_ipv6_dialer()
    {
        using var owner = DeviceIdentity.Create();
        using var joiner = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Entry(owner, PeerRole.Owner), Entry(joiner, PeerRole.ReadWrite) });
        var server = new SyncServer(new ReplicaContentProvider(new VaultReplica(owner.DeviceId), new ChangeSigner(owner)));

        using var lan = new SyncListener(owner, server, id => peers.Find(id) is not null);
        var bound = lan.Start(new IPEndPoint(IPAddress.IPv6Any, 0));      // dual-stack

        var v6 = new IPEndPoint(IPAddress.IPv6Loopback, bound.Port);
        using var conn = await PeerConnector.ConnectAsync(joiner, owner.DeviceId, new[] { v6 });
        Assert.Equal(owner.DeviceId, conn.RemoteDeviceId);
    }
}
