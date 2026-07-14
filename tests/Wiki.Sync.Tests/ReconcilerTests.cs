// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class ReconcilerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 9, 0, 0, TimeSpan.Zero);
    private readonly Reconciler _rec = new(new Diff3MergeAdapter());

    /// <summary>Serves content for the peer's live files (what a transport would fetch on demand).</summary>
    private sealed class DictContent(VaultReplica peer) : IRemoteContent
    {
        public string? Fetch(string path) => peer.Read(path);
    }

    [Fact]
    public void Adopts_a_remote_only_file()
    {
        var local = new VaultReplica("A");
        var peer = new VaultReplica("B");
        peer.Put("New.md", "from B");

        var report = _rec.Reconcile(local, "B", peer.Index, new DictContent(peer), Now);

        Assert.Equal("from B", local.Read("New.md"));
        Assert.Equal(1, report.Adopted);
    }

    [Fact]
    public void Adopts_a_remote_only_tombstone_without_content()
    {
        var local = new VaultReplica("A");
        var peer = new VaultReplica("B");
        peer.Put("Gone.md", "x");
        peer.Delete("Gone.md");

        _rec.Reconcile(local, "B", peer.Index, new DictContent(peer), Now);

        Assert.True(local.Find("Gone.md")!.Deleted);
        Assert.Null(local.Read("Gone.md"));
    }

    [Fact]
    public void Pulls_when_remote_dominates()
    {
        // A has v1 of the note; B has A's v1 then edits it to v2 → B dominates → A pulls B's content.
        var local = new VaultReplica("A");
        local.Put("Note.md", "v1");
        var peer = new VaultReplica("B");
        peer.ApplyReconciled("Note.md", "v1", local.Find("Note.md")!.Version, false);
        peer.Put("Note.md", "v2");

        _rec.Reconcile(local, "B", peer.Index, new DictContent(peer), Now);

        Assert.Equal("v2", local.Read("Note.md"));
    }

    [Fact]
    public void Does_nothing_when_local_dominates_or_is_equal()
    {
        var local = new VaultReplica("A");
        local.Put("Note.md", "v2");
        local.Put("Note.md", "v2b");                      // A:2
        var peer = new VaultReplica("B");
        peer.ApplyReconciled("Note.md", "v1", VersionVector.Empty.Increment("A"), false);  // A:1 (older)

        var report = _rec.Reconcile(local, "B", peer.Index, new DictContent(peer), Now);

        Assert.Equal("v2b", local.Read("Note.md"));       // unchanged; peer will pull from us
        Assert.Equal(0, report.Adopted);
    }

    [Fact]
    public void Concurrent_disjoint_edits_auto_merge_via_3way()
    {
        // Shared base "one/two/three"; A edits line 1, B edits line 3 → clean 3-way merge.
        var local = new VaultReplica("A");
        local.ApplyReconciled("Note.md", "one\ntwo\nthree", VersionVector.Empty.Increment("S"), false);
        var peer = new VaultReplica("B");
        peer.ApplyReconciled("Note.md", "one\ntwo\nthree", VersionVector.Empty.Increment("S"), false);

        local.Put("Note.md", "ONE\ntwo\nthree");   // A bumps A
        peer.Put("Note.md", "one\ntwo\nTHREE");    // B bumps B → concurrent

        var report = _rec.Reconcile(local, "B", peer.Index, new DictContent(peer), Now);

        Assert.Equal("ONE\ntwo\nTHREE", local.Read("Note.md"));
        Assert.Equal(1, report.Merged);
    }

    [Fact]
    public void Concurrent_same_line_edits_make_a_conflict_copy_with_a_deterministic_winner()
    {
        var local = new VaultReplica("A");
        local.ApplyReconciled("Note.md", "base", VersionVector.Empty.Increment("S"), false);
        var peer = new VaultReplica("B");
        peer.ApplyReconciled("Note.md", "base", VersionVector.Empty.Increment("S"), false);

        local.Put("Note.md", "A wins");
        peer.Put("Note.md", "B loses");

        var report = _rec.Reconcile(local, "B", peer.Index, new DictContent(peer), Now);

        Assert.Equal("A wins", local.Read("Note.md"));                 // "A" < "B" ordinally → A is main
        var copy = "Note (conflicted copy, B 2026-06-30).md";
        Assert.Equal("B loses", local.Read(copy));                     // B's content preserved as a copy
        Assert.Equal(1, report.Conflicted);
    }

    [Fact]
    public void Add_add_without_a_common_base_makes_a_conflict_copy()
    {
        // Both create Note.md independently → concurrent, no shared base → conflict copy.
        var local = new VaultReplica("A");
        local.Put("Note.md", "A body");
        var peer = new VaultReplica("B");
        peer.Put("Note.md", "B body");

        var report = _rec.Reconcile(local, "B", peer.Index, new DictContent(peer), Now);

        Assert.Equal("A body", local.Read("Note.md"));
        Assert.Equal("B body", local.Read("Note (conflicted copy, B 2026-06-30).md"));
        Assert.Equal(1, report.Conflicted);
    }

    [Fact]
    public void Delete_versus_edit_keeps_the_edit()
    {
        var local = new VaultReplica("A");
        local.ApplyReconciled("Note.md", "base", VersionVector.Empty.Increment("S"), false);
        var peer = new VaultReplica("B");
        peer.ApplyReconciled("Note.md", "base", VersionVector.Empty.Increment("S"), false);

        local.Delete("Note.md");          // A deletes
        peer.Put("Note.md", "kept edit"); // B edits → concurrent

        var report = _rec.Reconcile(local, "B", peer.Index, new DictContent(peer), Now);

        Assert.Equal("kept edit", local.Read("Note.md"));   // edit wins, delete dropped
        Assert.Equal(1, report.Resolved);
    }

    [Fact]
    public void Both_deleted_concurrently_stays_deleted()
    {
        var local = new VaultReplica("A");
        local.ApplyReconciled("Note.md", "base", VersionVector.Empty.Increment("S"), false);
        var peer = new VaultReplica("B");
        peer.ApplyReconciled("Note.md", "base", VersionVector.Empty.Increment("S"), false);

        local.Delete("Note.md");
        peer.Delete("Note.md");

        var report = _rec.Reconcile(local, "B", peer.Index, new DictContent(peer), Now);

        Assert.True(local.Find("Note.md")!.Deleted);
        Assert.Equal(1, report.Resolved);
    }
}
