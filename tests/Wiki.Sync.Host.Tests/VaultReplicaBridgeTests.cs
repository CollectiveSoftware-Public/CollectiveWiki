// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Host.Tests;

public class VaultReplicaBridgeTests
{
    [Fact]
    public void Seed_puts_every_note_and_skips_already_known_paths()
    {
        var vault = new FakeVault(new() { ["A.md"] = "alpha", ["B.md"] = "beta" });
        var replica = new VaultReplica("dev");
        replica.Put("A.md", "already");   // known → not re-seeded

        new VaultReplicaBridge(vault).SeedFromVault(replica);

        Assert.Equal("already", replica.Read("A.md")); // untouched
        Assert.Equal("beta", replica.Read("B.md"));    // seeded
    }

    [Fact]
    public void Flush_writes_live_notes_deletes_tombstones_and_reports_changes()
    {
        var vault = new FakeVault(new() { ["Keep.md"] = "old", ["Gone.md"] = "bye" });
        var replica = new VaultReplica("dev");
        replica.ApplyReconciled("Keep.md", "new", VersionVector.Empty.Increment("peer"), false);
        replica.ApplyReconciled("Fresh.md", "hi", VersionVector.Empty.Increment("peer"), false);
        replica.ApplyReconciled("Gone.md", null, VersionVector.Empty.Increment("peer"), true);

        var changed = new VaultReplicaBridge(vault).FlushToVault(replica);

        Assert.Equal("new", vault.ReadAllText("Keep.md"));
        Assert.Equal("hi", vault.ReadAllText("Fresh.md"));
        Assert.False(vault.Exists("Gone.md"));
        Assert.Contains("Keep.md", changed);
        Assert.Contains("Fresh.md", changed);
        Assert.Contains("Gone.md", changed);
    }
}
