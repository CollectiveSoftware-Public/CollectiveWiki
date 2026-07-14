// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;
using Wiki.Sync.Transport;

namespace Wiki.Sync.Transport.Tests;

/// <summary>The WAN gate: full CollectiveWiki sync — mutual TLS + Plan B authorization + reconcile — carried
/// end-to-end over a CollectiveRelay-forwarded stream, with no LAN path. The relay only ever forwards
/// ciphertext (proven structurally in the CollectiveRelay repo's own TLS-forwarding gate).</summary>
public class RelayForwardedSyncGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static AuthenticatingReconciler NewReconciler() => new(new Reconciler(new Diff3MergeAdapter()), new ChangeVerifier());
    private static AuthorizedPeer Entry(DeviceIdentity id, PeerRole role) => new(id.DeviceId, id.PublicKey, role, "n", "e");

    [Fact]
    public async Task A_joiner_syncs_the_owners_vault_over_the_relay_with_no_lan_path()
    {
        using var owner = DeviceIdentity.Create();
        using var joiner = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Entry(owner, PeerRole.Owner), Entry(joiner, PeerRole.ReadWrite) });

        var ownerReplica = new VaultReplica(owner.DeviceId);
        ownerReplica.Put("Remote.md", "synced across the internet");
        var server = new SyncServer(new ReplicaContentProvider(ownerReplica, new ChangeSigner(owner)));

        using var relay = new TestRelay();
        var relayEp = relay.Start();
        using var ownerListener = new RelaySyncListener(owner, server, id => peers.Find(id) is not null);
        await ownerListener.StartAsync(relayEp.Address.ToString(), relayEp.Port);
        await relay.WaitForRegistrationAsync(owner.DeviceId);

        var joinerReplica = new VaultReplica(joiner.DeviceId);
        var relayEndpoint = new RelayEndpoint(relayEp.Address.ToString(), relayEp.Port);
        using var conn = await PeerConnector.ConnectAsync(joiner, owner.DeviceId, lanEndpoint: null, relayEndpoint);
        var report = await new SyncClient(NewReconciler())
            .PullAsync(conn.Stream, conn.RemoteDeviceId, joinerReplica, peers, owner.DeviceId, Now);

        Assert.Equal(1, report.Inner.Adopted);
        Assert.Equal("synced across the internet", joinerReplica.Read("Remote.md"));
    }

    [Fact]
    public async Task A_stranger_off_the_roster_cannot_sync_over_the_relay()
    {
        using var owner = DeviceIdentity.Create();
        using var stranger = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Entry(owner, PeerRole.Owner) });

        var ownerReplica = new VaultReplica(owner.DeviceId);
        ownerReplica.Put("Secret.md", "not for strangers");
        var server = new SyncServer(new ReplicaContentProvider(ownerReplica, new ChangeSigner(owner)));

        using var relay = new TestRelay();
        var relayEp = relay.Start();
        using var ownerListener = new RelaySyncListener(owner, server, id => peers.Find(id) is not null);
        await ownerListener.StartAsync(relayEp.Address.ToString(), relayEp.Port);
        await relay.WaitForRegistrationAsync(owner.DeviceId);

        var relayEndpoint = new RelayEndpoint(relayEp.Address.ToString(), relayEp.Port);
        var strangerReplica = new VaultReplica(stranger.DeviceId);

        // TLS-1.3: the server rejecting the client cert may not fault the client handshake, so assert the
        // end-to-end invariant — the stranger cannot SYNC (dial + pull throws on the torn-down stream).
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var conn = await PeerConnector.ConnectAsync(stranger, owner.DeviceId, lanEndpoint: null, relayEndpoint);
            await new SyncClient(NewReconciler())
                .PullAsync(conn.Stream, conn.RemoteDeviceId, strangerReplica, peers, owner.DeviceId, Now);
        });
        Assert.Null(strangerReplica.Read("Secret.md"));
    }

    [Fact]
    public async Task Dialing_a_device_not_registered_on_the_relay_is_refused()
    {
        using var joiner = DeviceIdentity.Create();
        using var absent = DeviceIdentity.Create();
        using var relay = new TestRelay();
        var relayEp = relay.Start();
        var relayEndpoint = new RelayEndpoint(relayEp.Address.ToString(), relayEp.Port);

        await Assert.ThrowsAsync<IOException>(async () =>
            await PeerConnector.ConnectAsync(joiner, absent.DeviceId, lanEndpoint: null, relayEndpoint));
    }
}
