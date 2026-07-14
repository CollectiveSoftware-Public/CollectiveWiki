// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class VersionVectorTests
{
    [Fact]
    public void Empty_reads_zero_for_any_device() => Assert.Equal(0, VersionVector.Empty["A"]);

    [Fact]
    public void Increment_bumps_only_that_device()
    {
        var v = VersionVector.Empty.Increment("A").Increment("A").Increment("B");
        Assert.Equal(2, v["A"]);
        Assert.Equal(1, v["B"]);
        Assert.Equal(0, v["C"]);
    }

    [Fact]
    public void Equal_vectors_compare_equal()
    {
        var a = VersionVector.Empty.Increment("A");
        var b = VersionVector.Empty.Increment("A");
        Assert.Equal(VectorOrdering.Equal, a.CompareTo(b));
    }

    [Fact]
    public void A_strictly_ahead_dominates()
    {
        var a = VersionVector.Empty.Increment("A").Increment("A");
        var b = VersionVector.Empty.Increment("A");
        Assert.Equal(VectorOrdering.Dominates, a.CompareTo(b));
        Assert.Equal(VectorOrdering.DominatedBy, b.CompareTo(a));
    }

    [Fact]
    public void Disjoint_bumps_are_concurrent()
    {
        var a = VersionVector.Empty.Increment("A");
        var b = VersionVector.Empty.Increment("B");
        Assert.Equal(VectorOrdering.Concurrent, a.CompareTo(b));
    }

    [Fact]
    public void Merge_takes_the_elementwise_max()
    {
        var a = VersionVector.Empty.Increment("A").Increment("A");      // A:2
        var b = VersionVector.Empty.Increment("A").Increment("B");      // A:1, B:1
        var m = a.Merge(b);
        Assert.Equal(2, m["A"]);
        Assert.Equal(1, m["B"]);
        Assert.Equal(VectorOrdering.Dominates, m.CompareTo(a));         // merge dominates both inputs
        Assert.Equal(VectorOrdering.Dominates, m.CompareTo(b));
    }

    [Fact]
    public void Mutating_the_source_dictionary_after_construction_does_not_leak_in()
    {
        var source = new Dictionary<string, long> { ["A"] = 1 };
        var v = new VersionVector(source);
        source["B"] = 5;                 // mutate the original after construction
        Assert.Equal(0, v["B"]);         // the vector must not see it (defensive copy)
        Assert.Equal(1, v["A"]);
    }
}
