// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Wiki.Sync;

namespace Wiki.Sync.Host.Tests;

public class PeersAndKeyRingStoreTests
{
    [Fact]
    public void Roster_round_trips_and_still_verifies_against_the_pinned_owner()
    {
        using var owner = DeviceIdentity.Create();
        using var collab = DeviceIdentity.Create();
        var list = AuthorizedPeersList.Sign(owner, new[]
        {
            new AuthorizedPeer(owner.DeviceId, owner.PublicKey, PeerRole.Owner, "O", "o@x"),
            new AuthorizedPeer(collab.DeviceId, collab.PublicKey, PeerRole.ReadWrite, "C", "c@x"),
        });

        var store = new InMemorySyncStore();
        new AuthorizedPeersStore(store).Save(list);
        var reloaded = new AuthorizedPeersStore(store).Load()!;

        Assert.True(reloaded.Verify(owner.DeviceId));
        Assert.Equal(PeerRole.ReadWrite, reloaded.RoleOf(collab.DeviceId));
    }

    [Fact]
    public void Key_ring_round_trips_current_epoch_and_key_sealed_at_rest()
    {
        var sealer = new AtRestSealer(new FakeSecretStore());
        var ring = VaultKeyRing.Start(new ContentKeySealer());
        ring.Rotate();   // epoch 1
        var expected = ring.Current;

        var store = new InMemorySyncStore();
        new KeyRingStore(store, sealer).Save(ring);
        var reloaded = new KeyRingStore(store, sealer).Load(new ContentKeySealer())!;

        Assert.Equal(expected.Epoch, reloaded.Current.Epoch);
        Assert.Equal(expected.Key, reloaded.Current.Key);
        Assert.Equal("CWK1", Encoding.ASCII.GetString(store.ReadBytes("keyring.bin")!, 0, 4)); // sealed at rest
    }
}
