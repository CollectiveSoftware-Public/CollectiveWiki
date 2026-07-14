// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using System.Net.Sockets;
using Wiki.Sync;
using Wiki.Sync.Transport;

namespace Wiki.Sync.Transport.Tests;

public class TlsLanSyncGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private static AuthenticatingReconciler NewReconciler()
        => new(new Reconciler(new Diff3MergeAdapter()), new ChangeVerifier());
    private static AuthorizedPeer Entry(DeviceIdentity id, PeerRole role)
        => new(id.DeviceId, id.PublicKey, role, "n", "e");

    private static async Task<TlsPeerConnection> DialAsync(IPEndPoint endpoint, DeviceIdentity self, string expectedServer)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(endpoint);
        return await TlsPeerConnection.AuthenticateClientAsync(tcp.GetStream(), self, expectedServer);
    }

    [Fact]
    public async Task A_joiner_syncs_the_owners_vault_over_real_mutual_tls()
    {
        using var owner = DeviceIdentity.Create();
        using var joiner = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Entry(owner, PeerRole.Owner), Entry(joiner, PeerRole.ReadWrite) });

        var ownerReplica = new VaultReplica(owner.DeviceId);
        ownerReplica.Put("Shared.md", "the shared note");
        var server = new SyncServer(new ReplicaContentProvider(ownerReplica, new ChangeSigner(owner)));
        using var listener = new SyncListener(owner, server, id => peers.Find(id) is not null);
        var endpoint = listener.Start(new IPEndPoint(IPAddress.Loopback, 0));

        var joinerReplica = new VaultReplica(joiner.DeviceId);
        using var conn = await DialAsync(endpoint, joiner, owner.DeviceId);
        var report = await new SyncClient(NewReconciler())
            .PullAsync(conn.Stream, conn.RemoteDeviceId, joinerReplica, peers, owner.DeviceId, Now);

        Assert.Equal(1, report.Inner.Adopted);
        Assert.Equal("the shared note", joinerReplica.Read("Shared.md"));
    }

    [Fact]
    public async Task A_stranger_not_on_the_roster_cannot_sync()
    {
        using var owner = DeviceIdentity.Create();
        using var stranger = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Entry(owner, PeerRole.Owner) });

        var server = new SyncServer(new ReplicaContentProvider(new VaultReplica(owner.DeviceId), new ChangeSigner(owner)));
        using var listener = new SyncListener(owner, server, id => peers.Find(id) is not null);
        var endpoint = listener.Start(new IPEndPoint(IPAddress.Loopback, 0));

        // The server rejects the stranger's client certificate (not on the roster). Under TLS 1.3 that
        // rejection may not fault the client handshake itself, but the torn-down stream means the stranger
        // can never complete a pull — the security property that actually matters.
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var conn = await DialAsync(endpoint, stranger, owner.DeviceId);
            var strangerReplica = new VaultReplica(stranger.DeviceId);
            await new SyncClient(NewReconciler())
                .PullAsync(conn.Stream, conn.RemoteDeviceId, strangerReplica, peers, owner.DeviceId, Now);
        });
    }

    [Fact]
    public async Task A_joiner_pinning_the_wrong_owner_id_refuses_to_connect()
    {
        using var owner = DeviceIdentity.Create();
        using var joiner = DeviceIdentity.Create();
        using var impostorExpectation = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Entry(owner, PeerRole.Owner), Entry(joiner, PeerRole.ReadWrite) });

        var server = new SyncServer(new ReplicaContentProvider(new VaultReplica(owner.DeviceId), new ChangeSigner(owner)));
        using var listener = new SyncListener(owner, server, id => peers.Find(id) is not null);
        var endpoint = listener.Start(new IPEndPoint(IPAddress.Loopback, 0));

        // The joiner pins an id the server will never present → the joiner aborts the handshake.
        await Assert.ThrowsAnyAsync<Exception>(async () => await DialAsync(endpoint, joiner, impostorExpectation.DeviceId));
    }

    [Fact]
    public async Task Two_peers_converge_over_real_tls_including_each_others_edits()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Entry(owner, PeerRole.Owner), Entry(alice, PeerRole.ReadWrite) });

        var ownerReplica = new VaultReplica(owner.DeviceId);
        var aliceReplica = new VaultReplica(alice.DeviceId);
        ownerReplica.Put("Owner.md", "owned");
        aliceReplica.Put("Alice.md", "aliced");

        using var ownerListener = new SyncListener(owner,
            new SyncServer(new ReplicaContentProvider(ownerReplica, new ChangeSigner(owner))), id => peers.Find(id) is not null);
        using var aliceListener = new SyncListener(alice,
            new SyncServer(new ReplicaContentProvider(aliceReplica, new ChangeSigner(alice))), id => peers.Find(id) is not null);
        var ownerEp = ownerListener.Start(new IPEndPoint(IPAddress.Loopback, 0));
        var aliceEp = aliceListener.Start(new IPEndPoint(IPAddress.Loopback, 0));

        // Pull both directions until fixpoint, over real TLS each round.
        for (int round = 0; round < 4; round++)
        {
            using (var toAlice = await DialAsync(aliceEp, owner, alice.DeviceId))
                await new SyncClient(NewReconciler())
                    .PullAsync(toAlice.Stream, toAlice.RemoteDeviceId, ownerReplica, peers, owner.DeviceId, Now);
            using (var toOwner = await DialAsync(ownerEp, alice, owner.DeviceId))
                await new SyncClient(NewReconciler())
                    .PullAsync(toOwner.Stream, toOwner.RemoteDeviceId, aliceReplica, peers, owner.DeviceId, Now);
        }

        Assert.Equal("owned", aliceReplica.Read("Owner.md"));
        Assert.Equal("aliced", ownerReplica.Read("Alice.md"));
    }
}
