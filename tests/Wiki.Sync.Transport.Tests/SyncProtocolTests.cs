// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;
using Wiki.Sync.Transport;

namespace Wiki.Sync.Transport.Tests;

public class SyncProtocolTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private static AuthenticatingReconciler NewReconciler()
        => new(new Reconciler(new Diff3MergeAdapter()), new ChangeVerifier());

    private static AuthorizedPeer Entry(DeviceIdentity id, PeerRole role)
        => new(id.DeviceId, id.PublicKey, role, "n", "e");

    [Fact]
    public async Task A_puller_adopts_an_authorized_servers_notes_over_the_stream()
    {
        using var owner = DeviceIdentity.Create();
        using var joiner = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Entry(owner, PeerRole.Owner), Entry(joiner, PeerRole.ReadWrite) });

        var ownerReplica = new VaultReplica(owner.DeviceId);
        ownerReplica.Put("Welcome.md", "hello from owner");
        var server = new SyncServer(new ReplicaContentProvider(ownerReplica, new ChangeSigner(owner)));

        var joinerReplica = new VaultReplica(joiner.DeviceId);
        var client = new SyncClient(NewReconciler());

        var (s, c) = Loopback.TcpPair();
        AuthenticatedReport report;
        using (s) using (c)
        {
            var serve = server.ServeAsync(s);
            report = await client.PullAsync(c, owner.DeviceId, joinerReplica, peers, owner.DeviceId, Now);
            await serve;
        }
        Assert.Equal(1, report.Inner.Adopted);
        Assert.Equal("hello from owner", joinerReplica.Read("Welcome.md"));
    }

    [Fact]
    public async Task A_read_only_servers_pushed_change_is_rejected_by_the_puller()
    {
        using var owner = DeviceIdentity.Create();
        using var reader = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Entry(owner, PeerRole.Owner), Entry(reader, PeerRole.ReadOnly) });

        var readerReplica = new VaultReplica(reader.DeviceId);
        readerReplica.Put("Sneaky.md", "should not propagate");
        var server = new SyncServer(new ReplicaContentProvider(readerReplica, new ChangeSigner(reader)));

        var ownerReplica = new VaultReplica(owner.DeviceId);
        var client = new SyncClient(NewReconciler());

        var (s, c) = Loopback.TcpPair();
        AuthenticatedReport report;
        using (s) using (c)
        {
            var serve = server.ServeAsync(s);
            report = await client.PullAsync(c, reader.DeviceId, ownerReplica, peers, owner.DeviceId, Now);
            await serve;
        }
        Assert.Equal(1, report.ReadOnlyDenied);
        Assert.Null(ownerReplica.Read("Sneaky.md"));
    }
}
