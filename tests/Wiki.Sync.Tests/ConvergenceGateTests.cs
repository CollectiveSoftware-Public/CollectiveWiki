// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class ConvergenceGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Independent_creates_on_each_side_both_propagate()
    {
        var a = new VaultReplica("A");
        var b = new VaultReplica("B");
        a.Put("FromA.md", "a-body");
        b.Put("FromB.md", "b-body");

        SyncSimulator.RunToFixpoint(a, b, Now);

        SyncSimulator.AssertConverged(a, b);
        Assert.Equal("a-body", b.Read("FromA.md"));
        Assert.Equal("b-body", a.Read("FromB.md"));
    }

    [Fact]
    public void Concurrent_disjoint_edits_converge_to_the_merged_text()
    {
        var a = new VaultReplica("A");
        var b = new VaultReplica("B");
        // Establish a shared base first.
        a.Put("Note.md", "one\ntwo\nthree");
        SyncSimulator.RunToFixpoint(a, b, Now);

        a.Put("Note.md", "ONE\ntwo\nthree");
        b.Put("Note.md", "one\ntwo\nTHREE");
        SyncSimulator.RunToFixpoint(a, b, Now);

        SyncSimulator.AssertConverged(a, b);
        Assert.Equal("ONE\ntwo\nTHREE", a.Read("Note.md"));
    }

    [Fact]
    public void Concurrent_same_line_edits_converge_with_a_shared_conflict_copy()
    {
        var a = new VaultReplica("A");
        var b = new VaultReplica("B");
        a.Put("Note.md", "base");
        SyncSimulator.RunToFixpoint(a, b, Now);

        a.Put("Note.md", "A version");
        b.Put("Note.md", "B version");
        SyncSimulator.RunToFixpoint(a, b, Now);

        SyncSimulator.AssertConverged(a, b);
        Assert.Equal("A version", a.Read("Note.md"));                       // deterministic winner (A < B)
        var copy = "Note (conflicted copy, B 2026-06-30).md";
        Assert.Equal("B version", a.Read(copy));
        Assert.Equal("B version", b.Read(copy));                           // copy exists on both peers
    }

    [Fact]
    public void A_delete_propagates_and_does_not_resurrect()
    {
        var a = new VaultReplica("A");
        var b = new VaultReplica("B");
        a.Put("Note.md", "body");
        SyncSimulator.RunToFixpoint(a, b, Now);   // both have it

        a.Delete("Note.md");
        SyncSimulator.RunToFixpoint(a, b, Now);

        SyncSimulator.AssertConverged(a, b);
        Assert.Null(a.Read("Note.md"));
        Assert.Null(b.Read("Note.md"));
        Assert.True(b.Find("Note.md")!.Deleted);  // tombstone, not absent → cannot be resurrected
    }

    [Fact]
    public void Reconcile_is_order_independent()
    {
        // Same concurrent same-line scenario as above, but drive the reconcile passes in the REVERSED
        // peer order (b reconciles before a each round). The deterministic winner (A, ordinally smaller)
        // and the shared conflict copy must be identical regardless of which peer reconciles first.
        var a = new VaultReplica("A");
        var b = new VaultReplica("B");
        a.Put("Note.md", "base");
        SyncSimulator.RunToFixpoint(b, a, Now);   // reversed pass order

        a.Put("Note.md", "A version");
        b.Put("Note.md", "B version");
        SyncSimulator.RunToFixpoint(b, a, Now);   // reversed pass order

        SyncSimulator.AssertConverged(a, b);
        Assert.Equal("A version", a.Read("Note.md"));   // winner is independent of reconcile order
        Assert.Equal("A version", b.Read("Note.md"));
        var copy = "Note (conflicted copy, B 2026-06-30).md";
        Assert.Equal("B version", a.Read(copy));
        Assert.Equal("B version", b.Read(copy));        // shared copy on both peers
    }

    [Fact]
    public void Concurrent_add_of_the_same_path_without_a_base_converges_with_a_shared_conflict_copy()
    {
        // Both peers independently create the same path with no shared ancestor → deterministic
        // conflict copy (ordinally-smaller device wins the main file), converging on both peers.
        var a = new VaultReplica("A");
        var b = new VaultReplica("B");
        a.Put("Note.md", "A body");
        b.Put("Note.md", "B body");
        SyncSimulator.RunToFixpoint(a, b, Now);

        SyncSimulator.AssertConverged(a, b);
        Assert.Equal("A body", a.Read("Note.md"));
        var copy = "Note (conflicted copy, B 2026-06-30).md";
        Assert.Equal("B body", a.Read(copy));
        Assert.Equal("B body", b.Read(copy));
    }
}
