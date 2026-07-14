// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Search;
using Xunit;

namespace Wiki.Core.Tests;

public class SnippetBuilderTests
{
    [Fact]
    public void Builds_a_snippet_per_matching_line_with_offsets()
    {
        const string note = "intro\nthe quick brown fox\nmiddle\nanother quick line";
        var s = SnippetBuilder.Build(note, "quick", maxSnippets: 5);
        Assert.Equal(2, s.Count);
        Assert.Equal(1, s[0].Line);
        Assert.Equal("the quick brown fox", s[0].Text);
        Assert.Equal(4, s[0].MatchStart);
        Assert.Equal(5, s[0].MatchLength);
        Assert.Equal(3, s[1].Line);
    }

    [Fact]
    public void Match_offset_is_relative_to_the_trimmed_line()
    {
        var s = SnippetBuilder.Build("    indented quick line", "quick", maxSnippets: 1);
        Assert.Single(s);
        Assert.Equal("indented quick line", s[0].Text);
        Assert.Equal(9, s[0].MatchStart);   // "quick" starts at index 9 within the trimmed text
    }

    [Fact]
    public void Caps_the_number_of_snippets()
        => Assert.Single(SnippetBuilder.Build("x\nx\nx", "x", maxSnippets: 1));

    [Fact]
    public void No_match_yields_no_snippets()
        => Assert.Empty(SnippetBuilder.Build("nothing here", "zzz", maxSnippets: 3));

    [Fact]
    public void Empty_query_yields_no_snippets()
        => Assert.Empty(SnippetBuilder.Build("anything", "", maxSnippets: 3));
}
