// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using Wiki.Sync;
using Wiki.Sync.Transport;

namespace Wiki.Sync.Host.Tests;

public class PairingOverWireGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static AuthenticatingReconciler NewReconciler() => new(new Reconciler(new Diff3MergeAdapter()), new ChangeVerifier());

    [Fact]
    public async Task Two_devices_pair_over_the_wire_then_sync()
    {
        // Owner shares a seeded vault + mints an invite.
        var ownerVault = new FakeVault(new() { ["Shared.md"] = "hello over the wire" });
        var ownerStore = new InMemorySyncStore();
        var owner = VaultSyncHost.Open(ownerVault, ownerStore, new FakeSecretStore(), new ContentKeySealer(), NewReconciler());
        owner.ShareVault("Ada", "ada@x");
        var invite = owner.AddCollaborator(PeerRole.ReadWrite, Now.AddHours(1));

        // Joiner starts with an empty vault and no roster.
        var joinerVault = new FakeVault();
        var joinerStore = new InMemorySyncStore();
        var joiner = VaultSyncHost.Open(joinerVault, joinerStore, new FakeSecretStore(), new ContentKeySealer(), NewReconciler());
        Assert.Null(joiner.Peers);

        // Owner serves pairing (accept-any TLS, token-gated); joiner dials pinning the owner and pairs over the wire.
        using (var pairing = new PairingListener(owner.Identity, (conn, ct) => owner.ServePairingAsync(conn, Now, ct)))
        {
            var pep = pairing.Start(new IPEndPoint(IPAddress.Loopback, 0));
            using var conn = await PeerConnector.ConnectAsync(joiner.Identity, owner.DeviceId, pep, relay: null);
            Assert.Equal(PairingOutcome.Accepted, await joiner.RequestPairingAsync(conn, invite, "Bob", "bob@x"));
        }

        Assert.Equal(PeerRole.ReadWrite, joiner.Peers!.RoleOf(joiner.DeviceId)); // adopted the owner-signed roster
        Assert.True(joiner.Peers!.Verify(owner.DeviceId));                       // pinned to the owner
        Assert.True(joinerStore.Exists("keyring.bin"));                          // received + persisted the content key

        // The paired joiner now syncs normally over the roster-gated transport.
        using (var syncListener = new SyncListener(owner.Identity, owner.BuildSyncServer(), owner.AcceptPeer))
        {
            var sep = syncListener.Start(new IPEndPoint(IPAddress.Loopback, 0));
            using var conn = await PeerConnector.ConnectAsync(joiner.Identity, owner.DeviceId, sep, relay: null);
            var report = await joiner.PullFromAsync(conn, Now);
            Assert.Equal(1, report.Inner.Adopted);
        }
        Assert.Equal("hello over the wire", joinerVault.ReadAllText("Shared.md"));
    }

    [Fact]
    public async Task A_stranger_without_a_valid_invite_token_is_rejected()
    {
        var owner = VaultSyncHost.Open(new FakeVault(), new InMemorySyncStore(), new FakeSecretStore(), new ContentKeySealer(), NewReconciler());
        owner.ShareVault("Ada", "ada@x");
        // A well-formed invite for a DIFFERENT vault/owner → the token won't match this owner's pending set.
        var bogusOwner = VaultSyncHost.Open(new FakeVault(), new InMemorySyncStore(), new FakeSecretStore(), new ContentKeySealer(), NewReconciler());
        bogusOwner.ShareVault("Eve", "eve@x");
        var bogusInvite = bogusOwner.AddCollaborator(PeerRole.ReadWrite, Now.AddHours(1));

        var joiner = VaultSyncHost.Open(new FakeVault(), new InMemorySyncStore(), new FakeSecretStore(), new ContentKeySealer(), NewReconciler());

        using var pairing = new PairingListener(owner.Identity, (conn, ct) => owner.ServePairingAsync(conn, Now, ct));
        var pep = pairing.Start(new IPEndPoint(IPAddress.Loopback, 0));
        using var conn = await PeerConnector.ConnectAsync(joiner.Identity, owner.DeviceId, pep, relay: null);

        // Joiner presents the bogus invite; the owner has no matching pending token → not Accepted, joiner stays rosterless.
        var outcome = await joiner.RequestPairingAsync(conn, bogusInvite, "Mallory", "m@x");
        Assert.NotEqual(PairingOutcome.Accepted, outcome);
        Assert.Null(joiner.Peers);
    }
}
