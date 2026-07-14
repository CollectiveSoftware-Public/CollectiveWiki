// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Host.Tests;

public class ReplicaStateStoreTests
{
    [Fact]
    public void Replica_state_round_trips_including_base_and_multi_device_vectors()
    {
        var replica = new VaultReplica("dev-a");
        replica.Put("Note.md", "hello");   // version {dev-a:1}, base still null (not yet synced)
        replica.ApplyReconciled("Synced.md", "shared",
            new VersionVector(new Dictionary<string, long> { ["dev-a"] = 1, ["dev-b"] = 2 }), false); // base = "shared"
        replica.Delete("Gone.md");         // tombstone

        var store = new InMemorySyncStore();
        new ReplicaStateStore(store).Save(replica);

        var restored = new VaultReplica("dev-a");
        new ReplicaStateStore(store).Load(restored);

        Assert.Equal("hello", restored.Read("Note.md"));
        Assert.Null(restored.BaseOf("Note.md"));            // base preserved as null, independent of content
        Assert.Equal("shared", restored.BaseOf("Synced.md")); // base preserved
        Assert.Equal(2, restored.Find("Synced.md")!.Version["dev-b"]);
        Assert.Null(restored.Read("Gone.md"));              // tombstone preserved
        Assert.True(restored.Find("Gone.md")!.Deleted);
    }
}
