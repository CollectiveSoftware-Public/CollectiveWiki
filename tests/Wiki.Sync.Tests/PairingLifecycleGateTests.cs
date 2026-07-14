// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class PairingLifecycleGateTests
{
    private static readonly Guid Vault = new("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Expiry = Now.AddHours(1);

    private readonly AuthenticatingReconciler _auth =
        new(new Reconciler(new Diff3MergeAdapter()), new ChangeVerifier());

    private sealed class Live(VaultReplica peer) : IRemoteContent
    {
        public string? Fetch(string path) => peer.Read(path);
    }

    [Fact]
    public void Pairing_grants_a_joiner_write_access_end_to_end()
    {
        using var owner = DeviceIdentity.Create();
        using var joiner = DeviceIdentity.Create();
        var coordinator = new PairingCoordinator(owner, Vault, "Owner", "o@x");

        // Before pairing, the joiner is not on the roster.
        Assert.Null(coordinator.CurrentList().RoleOf(joiner.DeviceId));

        // Owner shares an invite; the joiner pastes it and builds a signed join request.
        Assert.True(InviteCodec.TryParse(
            InviteCodec.Encode(coordinator.Issue(PeerRole.ReadWrite, Expiry)), out var invite));
        var request = JoinRequestFactory.Create(joiner, invite!, "Joiner", "j@x");

        var result = coordinator.Accept(request, Now);
        Assert.Equal(PairingOutcome.Accepted, result.Outcome);
        var peers = result.UpdatedList!;
        Assert.True(peers.Verify(owner.DeviceId));
        Assert.Equal(PeerRole.ReadWrite, peers.RoleOf(joiner.DeviceId));

        // The freshly authorized joiner's change is now adopted by the Plan B authenticating reconciler.
        var ownerReplica = new VaultReplica(owner.DeviceId);
        var joinerReplica = new VaultReplica(joiner.DeviceId);
        joinerReplica.Put("Joined.md", "hello from the new peer");
        var signed = new ChangeSigner(joiner).SignIndex(joinerReplica.Index);

        var report = _auth.Reconcile(
            ownerReplica, peers, owner.DeviceId, joiner.DeviceId, signed, new Live(joinerReplica), Now);

        Assert.Equal("hello from the new peer", ownerReplica.Read("Joined.md"));
        Assert.Equal(1, report.Inner.Adopted);
    }

    [Fact]
    public void Revocation_removes_write_access_and_rotation_locks_out_the_revoked_peer()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();     // stays
        using var mallory = DeviceIdentity.Create();   // gets revoked
        var coordinator = new PairingCoordinator(owner, Vault, "Owner", "o@x");

        coordinator.Accept(JoinRequestFactory.Create(alice, coordinator.Issue(PeerRole.ReadWrite, Expiry), "Alice", "a@x"), Now);
        coordinator.Accept(JoinRequestFactory.Create(mallory, coordinator.Issue(PeerRole.ReadWrite, Expiry), "Mallory", "m@x"), Now);

        // Owner distributes the content key to both; Mallory can unseal her copy today.
        var sealer = new ContentKeySealer();
        var keyRing = VaultKeyRing.Start(sealer);
        var mallorySealBefore = keyRing.SealCurrentFor(owner, coordinator.Peers)
            .Single(s => s.RecipientDeviceId == mallory.DeviceId);
        Assert.NotNull(sealer.Unseal(mallory, owner.PublicKey, owner.DeviceId, mallorySealBefore));

        // Revoke Mallory and rotate the content key.
        var revoked = coordinator.Revoke(mallory.DeviceId);
        Assert.Null(revoked.RoleOf(mallory.DeviceId));                       // off the roster
        Assert.Equal(PeerRole.ReadWrite, revoked.RoleOf(alice.DeviceId));    // Alice untouched
        keyRing.Rotate();
        var afterSeals = keyRing.SealCurrentFor(owner, coordinator.Peers);   // Peers no longer includes Mallory

        // Mallory is never sealed the new key...
        Assert.DoesNotContain(afterSeals, s => s.RecipientDeviceId == mallory.DeviceId);
        // ...Alice gets it and recovers exactly the rotated key...
        var aliceSeal = afterSeals.Single(s => s.RecipientDeviceId == alice.DeviceId);
        var aliceKey = sealer.Unseal(alice, owner.PublicKey, owner.DeviceId, aliceSeal);
        Assert.NotNull(aliceKey);
        Assert.Equal(keyRing.Current.Key, aliceKey!.Key);
        // ...and Mallory cannot unseal Alice's copy (different ECDH secret).
        Assert.Null(sealer.Unseal(mallory, owner.PublicKey, owner.DeviceId, aliceSeal));

        // Mallory's post-revocation change is now rejected by the authenticating reconciler.
        var ownerReplica = new VaultReplica(owner.DeviceId);
        var malloryReplica = new VaultReplica(mallory.DeviceId);
        malloryReplica.Put("Evil.md", "should not land");
        var signed = new ChangeSigner(mallory).SignIndex(malloryReplica.Index);
        var report = _auth.Reconcile(
            ownerReplica, revoked, owner.DeviceId, mallory.DeviceId, signed, new Live(malloryReplica), Now);

        Assert.Null(ownerReplica.Read("Evil.md"));
        Assert.Equal(1, report.Unauthorized);
    }
}
