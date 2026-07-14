// SPDX-License-Identifier: GPL-3.0-or-later
using Collective.Platform.Secrets;
using Wiki.Sync;

namespace Wiki.Sync.Host.Tests;

public class VaultSyncServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static AuthenticatingReconciler NewReconciler() => new(new Reconciler(new Diff3MergeAdapter()), new ChangeVerifier());

    private static VaultSyncService NewService(Dictionary<string, string>? notes = null)
        => VaultSyncHost.Open(new FakeVault(notes ?? new()), new InMemorySyncStore(),
            new FakeSecretStore(), new ContentKeySealer(), NewReconciler());

    [Fact]
    public void Share_makes_us_owner_seeds_the_replica_and_persists()
    {
        var vault = new FakeVault(new() { ["Home.md"] = "welcome" });
        var store = new InMemorySyncStore();
        var svc = VaultSyncHost.Open(vault, store, new FakeSecretStore(), new ContentKeySealer(), NewReconciler());

        svc.ShareVault("Ada", "ada@x");

        Assert.True(svc.IsOwner);
        Assert.Equal(svc.DeviceId, svc.OwnerDeviceId);
        Assert.True(svc.Peers!.Verify(svc.DeviceId));
        Assert.Equal("welcome", svc.Replica.Read("Home.md")); // seeded
        Assert.True(store.Exists("peers.json") && store.Exists("keyring.bin")
                    && store.Exists("state.json") && store.Exists("owner.json"));
    }

    [Fact]
    public void An_invite_round_trips_into_a_join_request_the_owner_accepts_and_the_joiner_adopts()
    {
        var owner = NewService();
        owner.ShareVault("Ada", "ada@x");
        var invite = owner.AddCollaborator(PeerRole.ReadWrite, Now.AddHours(1));

        var joiner = NewService();
        var request = joiner.CreateJoinRequest(invite, "Bob", "bob@x");
        Assert.NotNull(request);

        Assert.Equal(PairingOutcome.Accepted, owner.AcceptJoin(request!, Now));
        Assert.Equal(PeerRole.ReadWrite, owner.Peers!.RoleOf(joiner.DeviceId));

        Assert.True(joiner.AdoptRoster(owner.Peers!)); // joiner receives + verifies the signed roster
        Assert.Equal(PeerRole.ReadWrite, joiner.Peers!.RoleOf(joiner.DeviceId));
    }

    [Fact]
    public void Revoke_drops_the_collaborator_from_the_roster()
    {
        var owner = NewService();
        owner.ShareVault("Ada", "ada@x");
        var invite = owner.AddCollaborator(PeerRole.ReadWrite, Now.AddHours(1));
        var joiner = NewService();
        owner.AcceptJoin(joiner.CreateJoinRequest(invite, "Bob", "bob@x")!, Now);
        Assert.NotNull(owner.Peers!.RoleOf(joiner.DeviceId));

        owner.Revoke(joiner.DeviceId);
        Assert.Null(owner.Peers!.RoleOf(joiner.DeviceId));
    }

    [Fact]
    public void Owner_state_rehydrates_after_reopen_with_the_same_store_and_keystore()
    {
        var vault = new FakeVault(new() { ["A.md"] = "a" });
        var store = new InMemorySyncStore();
        ISecretStore secrets = new FakeSecretStore();

        var first = VaultSyncHost.Open(vault, store, secrets, new ContentKeySealer(), NewReconciler());
        first.ShareVault("Ada", "ada@x");

        var second = VaultSyncHost.Open(vault, store, secrets, new ContentKeySealer(), NewReconciler());
        Assert.True(second.IsOwner);
        Assert.Equal(first.DeviceId, second.DeviceId);

        // the rehydrated owner can still pair a new collaborator against a fresh invite
        var invite = second.AddCollaborator(PeerRole.ReadOnly, Now.AddHours(1));
        var joiner = NewService();
        Assert.Equal(PairingOutcome.Accepted, second.AcceptJoin(joiner.CreateJoinRequest(invite, "Bob", "b@x")!, Now));
    }
}
