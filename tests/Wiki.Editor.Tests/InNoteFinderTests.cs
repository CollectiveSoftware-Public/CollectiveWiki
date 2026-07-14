// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Editor;
using Xunit;

namespace Wiki.Editor.Tests;

public class InNoteFinderTests
{
    [Fact]
    public void Finds_all_case_insensitive_matches_in_order()
    {
        var m = InNoteFinder.Find("Foo foo fOo", "foo", matchCase: false);
        Assert.Equal(3, m.Count);
        Assert.Equal(0, m[0].Start);
        Assert.Equal(4, m[1].Start);
        Assert.Equal(8, m[2].Start);
        Assert.All(m, x => Assert.Equal(3, x.Length));
    }

    [Fact]
    public void Case_sensitive_search_respects_case()
    {
        var m = InNoteFinder.Find("Foo foo", "foo", matchCase: true);
        Assert.Single(m);
        Assert.Equal(4, m[0].Start);
    }

    [Fact]
    public void Empty_query_returns_no_matches()
        => Assert.Empty(InNoteFinder.Find("anything", "", matchCase: false));

    [Fact]
    public void Next_moves_forward_to_the_first_match_after_the_caret_and_wraps()
    {
        var m = InNoteFinder.Find("a x b x c x", "x", matchCase: false);   // starts 2,6,10
        Assert.Equal(1, InNoteFinder.Next(m, fromOffset: 3, forward: true));   // first > 3 is index 1 (@6)
        Assert.Equal(0, InNoteFinder.Next(m, fromOffset: 10, forward: true));  // past last → wrap to 0
    }

    [Fact]
    public void Next_backward_finds_the_last_match_before_the_caret_and_wraps()
    {
        var m = InNoteFinder.Find("a x b x c x", "x", matchCase: false);   // 2,6,10
        Assert.Equal(0, InNoteFinder.Next(m, fromOffset: 6, forward: false));  // last < 6 is index 0 (@2)
        Assert.Equal(2, InNoteFinder.Next(m, fromOffset: 0, forward: false));  // none before → wrap to last
    }

    [Fact]
    public void Next_on_an_empty_match_list_returns_negative_one()
        => Assert.Equal(-1, InNoteFinder.Next(InNoteFinder.Find("abc", "z", false), 0, forward: true));

    // ---- replace ----

    [Fact]
    public void Replace_all_replaces_every_match_case_insensitively()
    {
        string result = InNoteFinder.ReplaceAll("Cat cat CAT", "cat", "dog", matchCase: false, out int n);
        Assert.Equal("dog dog dog", result);
        Assert.Equal(3, n);
    }

    [Fact]
    public void Replace_all_with_match_case_leaves_other_casings()
    {
        string result = InNoteFinder.ReplaceAll("Cat cat", "cat", "dog", matchCase: true, out int n);
        Assert.Equal("Cat dog", result);
        Assert.Equal(1, n);
    }

    [Fact]
    public void Replace_all_with_an_empty_query_is_a_no_op()
    {
        string result = InNoteFinder.ReplaceAll("abc", "", "x", matchCase: false, out int n);
        Assert.Equal("abc", result);
        Assert.Equal(0, n);
    }

    [Fact]
    public void Replace_all_supports_a_longer_replacement()
    {
        string result = InNoteFinder.ReplaceAll("a b a", "a", "long", matchCase: false, out int n);
        Assert.Equal("long b long", result);
        Assert.Equal(2, n);
    }
}
