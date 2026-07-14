// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class Diff3MergeAdapterTests
{
    private readonly IThreeWayMerger _merger = new Diff3MergeAdapter();

    [Fact]
    public void Disjoint_changes_merge_cleanly()
    {
        // base: 3 lines; left changes line 1, right changes line 3 → no overlap → clean merge.
        var result = _merger.Merge(
            @base: "one\ntwo\nthree",
            left:  "ONE\ntwo\nthree",
            right: "one\ntwo\nTHREE");
        Assert.False(result.HasConflicts);
        Assert.Equal("ONE\ntwo\nTHREE", result.MergedText);
    }

    [Fact]
    public void Both_changing_the_same_line_conflicts()
    {
        var result = _merger.Merge(
            @base: "one\ntwo\nthree",
            left:  "one\nLEFT\nthree",
            right: "one\nRIGHT\nthree");
        Assert.True(result.HasConflicts);
    }

    [Fact]
    public void Identical_change_on_both_sides_is_clean()
    {
        var result = _merger.Merge("a\nb", "a\nB", "a\nB");
        Assert.False(result.HasConflicts);
        Assert.Equal("a\nB", result.MergedText);
    }

    [Fact]
    public void Crlf_input_is_normalised_before_merging()
    {
        var result = _merger.Merge("one\r\ntwo", "ONE\r\ntwo", "one\r\ntwo");
        Assert.False(result.HasConflicts);
        Assert.Equal("ONE\ntwo", result.MergedText);
    }
}
