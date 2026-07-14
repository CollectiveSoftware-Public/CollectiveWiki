// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class IdentityStoreTests
{
    [Fact]
    public void LoadOrCreate_generates_and_persists_on_first_call()
    {
        var store = new InMemoryIdentityStore();
        Assert.Null(store.Load());

        using var id = DeviceIdentityProvider.LoadOrCreate(store);

        Assert.NotNull(store.Load());   // persisted for next time
        Assert.Equal(52, id.DeviceId.Length);
    }

    [Fact]
    public void LoadOrCreate_returns_the_same_identity_on_second_call()
    {
        var store = new InMemoryIdentityStore();
        using var first = DeviceIdentityProvider.LoadOrCreate(store);
        using var second = DeviceIdentityProvider.LoadOrCreate(store);
        Assert.Equal(first.DeviceId, second.DeviceId);   // reloaded, not regenerated
    }

    [Fact]
    public void InMemory_store_round_trips_bytes()
    {
        var store = new InMemoryIdentityStore();
        store.Save(new byte[] { 1, 2, 3 });
        Assert.Equal(new byte[] { 1, 2, 3 }, store.Load());
    }
}
