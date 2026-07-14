// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Wiki.Sync;

namespace Wiki.Sync.Host.Tests;

public class IdentityStoreTests
{
    [Fact]
    public void Identity_persists_and_reloads_with_a_stable_device_id()
    {
        var store = new InMemorySyncStore();
        var idStore = new EncryptedFileIdentityStore(store, new AtRestSealer(new FakeSecretStore()));

        using var first = DeviceIdentityProvider.LoadOrCreate(idStore);
        using var second = DeviceIdentityProvider.LoadOrCreate(idStore);
        Assert.Equal(first.DeviceId, second.DeviceId);
    }

    [Fact]
    public void The_persisted_identity_is_sealed_at_rest_not_plaintext()
    {
        var store = new InMemorySyncStore();
        using var _ = DeviceIdentityProvider.LoadOrCreate(
            new EncryptedFileIdentityStore(store, new AtRestSealer(new FakeSecretStore())));

        var blob = store.ReadBytes("identity.bin")!;
        Assert.Equal("CWK1", Encoding.ASCII.GetString(blob, 0, 4)); // AES-GCM sealed, not a raw PFX
    }
}
