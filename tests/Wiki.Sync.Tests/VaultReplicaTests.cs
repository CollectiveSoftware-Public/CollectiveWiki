// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class VaultReplicaTests
{
    [Fact]
    public void Put_creates_a_live_entry_and_bumps_this_device()
    {
        var r = new VaultReplica("A");
        r.Put("Note.md", "hello");
        var e = r.Find("Note.md")!;
        Assert.False(e.Deleted);
        Assert.Equal(1, e.Version["A"]);
        Assert.Equal(ContentHash.Of("hello"), e.ContentHash);
        Assert.Equal("hello", r.Read("Note.md"));
    }

    [Fact]
    public void Repeated_Put_keeps_bumping_the_same_device()
    {
        var r = new VaultReplica("A");
        r.Put("Note.md", "v1");
        r.Put("Note.md", "v2");
        Assert.Equal(2, r.Find("Note.md")!.Version["A"]);
        Assert.Equal("v2", r.Read("Note.md"));
    }

    [Fact]
    public void Delete_tombstones_the_entry_and_drops_content()
    {
        var r = new VaultReplica("A");
        r.Put("Note.md", "hello");
        r.Delete("Note.md");
        var e = r.Find("Note.md")!;
        Assert.True(e.Deleted);
        Assert.Equal(2, e.Version["A"]);     // create then delete
        Assert.Null(r.Read("Note.md"));
    }

    [Fact]
    public void New_local_file_has_no_ancestor_base()
    {
        var r = new VaultReplica("A");
        r.Put("Note.md", "hello");
        Assert.Null(r.BaseOf("Note.md"));    // never synced with a peer yet
    }

    [Fact]
    public void ApplyReconciled_sets_content_version_and_base()
    {
        var r = new VaultReplica("A");
        var v = VersionVector.Empty.Increment("B");
        r.ApplyReconciled("Note.md", "fromB", v, deleted: false);
        Assert.Equal("fromB", r.Read("Note.md"));
        Assert.Equal("fromB", r.BaseOf("Note.md"));   // reconciled state becomes the new common ancestor
        Assert.Equal(VectorOrdering.Equal, r.Find("Note.md")!.Version.CompareTo(v));
    }

    [Fact]
    public void Index_lists_every_known_path_including_tombstones()
    {
        var r = new VaultReplica("A");
        r.Put("Live.md", "x");
        r.Put("Gone.md", "y");
        r.Delete("Gone.md");
        var paths = r.Index.Select(e => e.Path).OrderBy(p => p).ToArray();
        Assert.Equal(new[] { "Gone.md", "Live.md" }, paths);
        Assert.True(r.Find("Gone.md")!.Deleted);
    }

    [Fact]
    public void ApplyReconciled_on_delete_clears_base()
    {
        var r = new VaultReplica("A");
        r.ApplyReconciled("Gone.md", null, VersionVector.Empty.Increment("B"), deleted: true);
        Assert.True(r.Find("Gone.md")!.Deleted);
        Assert.Null(r.Read("Gone.md"));
        Assert.Null(r.BaseOf("Gone.md"));     // tombstone has no ancestor base
    }

    [Fact]
    public void Put_after_sync_leaves_ancestor_base_untouched()
    {
        var r = new VaultReplica("A");
        var v = VersionVector.Empty.Increment("B");
        r.ApplyReconciled("Note.md", "v1", v, deleted: false);   // synced state: base = "v1"
        r.Put("Note.md", "v2");                                  // local edit after sync
        Assert.Equal("v2", r.Read("Note.md"));
        Assert.Equal("v1", r.BaseOf("Note.md"));                 // ancestor must NOT change
    }

    [Fact]
    public void ConfirmBase_records_current_content_as_the_base_for_a_live_entry()
    {
        var r = new VaultReplica("A");
        r.Put("Note.md", "hello");
        Assert.Null(r.BaseOf("Note.md"));            // Put leaves the ancestor base untouched
        r.ConfirmBase("Note.md");
        Assert.Equal("hello", r.BaseOf("Note.md"));  // mutual agreement records the common ancestor
    }

    [Fact]
    public void ConfirmBase_clears_the_base_for_a_tombstoned_entry()
    {
        var r = new VaultReplica("A");
        r.ApplyReconciled("Note.md", "hello", VersionVector.Empty.Increment("B"), false);
        Assert.Equal("hello", r.BaseOf("Note.md"));
        r.Delete("Note.md");                         // Delete leaves the stale base in place
        r.ConfirmBase("Note.md");
        Assert.Null(r.BaseOf("Note.md"));            // a tombstone has no ancestor
    }

    [Fact]
    public void ConfirmBase_is_a_safe_no_op_for_an_unknown_path()
    {
        var r = new VaultReplica("A");
        r.ConfirmBase("Missing.md");                 // must not throw
        Assert.Null(r.BaseOf("Missing.md"));
    }
}
