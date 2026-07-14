// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Host.Tests;

public class VaultReplicaRefreshTests
{
    [Fact]
    public void Refresh_puts_new_and_changed_notes_and_tombstones_removed_ones()
    {
        var vault = new FakeVault(new() { ["A.md"] = "one", ["B.md"] = "two" });
        var replica = new VaultReplica("dev-1");
        var bridge = new VaultReplicaBridge(vault);
        bridge.SeedFromVault(replica);                 // A, B known

        vault.WriteAllText("A.md", "one-edited");        // change
        vault.Delete("B.md");                            // remove
        vault.WriteAllText("C.md", "three");             // add

        var changed = bridge.RefreshFromVault(replica);

        Assert.Contains("A.md", changed);
        Assert.Contains("B.md", changed);
        Assert.Contains("C.md", changed);
        Assert.Equal("one-edited", replica.Read("A.md"));
        Assert.True(replica.Find("B.md")!.Deleted);
        Assert.Null(replica.Read("B.md"));
        Assert.Equal("three", replica.Read("C.md"));

        Assert.Empty(bridge.RefreshFromVault(replica));  // idempotent: a second pass with no disk change is a no-op
    }
}
