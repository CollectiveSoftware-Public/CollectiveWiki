// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class VaultKeyRingTests
{
    private static AuthorizedPeer Peer(DeviceIdentity id)
        => new(id.DeviceId, id.PublicKey, PeerRole.ReadWrite, "n", "e");

    [Fact]
    public void Rotate_increments_the_epoch_and_changes_the_key()
    {
        var ring = VaultKeyRing.Start();
        var first = ring.Current;
        var second = ring.Rotate();

        Assert.Equal(first.Epoch + 1, second.Epoch);
        Assert.NotEqual(first.Key, second.Key);
        Assert.Equal(second, ring.Current);
    }

    [Fact]
    public void SealCurrentFor_seals_for_every_recipient_except_the_owner()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        using var bob = DeviceIdentity.Create();
        var ring = VaultKeyRing.Start();

        var seals = ring.SealCurrentFor(owner, new[] { Peer(owner), Peer(alice), Peer(bob) });

        Assert.Equal(2, seals.Count);
        Assert.DoesNotContain(seals, s => s.RecipientDeviceId == owner.DeviceId);
    }

    [Fact]
    public void The_sealed_current_key_round_trips_for_a_recipient()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var sealer = new ContentKeySealer();
        var ring = VaultKeyRing.Start(sealer);

        var seal = ring.SealCurrentFor(owner, new[] { Peer(alice) }).Single();
        var recovered = sealer.Unseal(alice, owner.PublicKey, owner.DeviceId, seal);

        Assert.NotNull(recovered);
        Assert.Equal(ring.Current.Key, recovered!.Key);
    }
}
