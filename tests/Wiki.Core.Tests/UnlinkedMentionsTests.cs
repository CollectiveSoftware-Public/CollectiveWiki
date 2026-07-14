// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Linq;
using Wiki.Core.Indexing;
using Xunit;

namespace Wiki.Core.Tests;

public class UnlinkedMentionsTests
{
    private static (string, string)[] Notes(params (string, string)[] n) => n;

    [Fact]
    public void Finds_a_whole_word_case_insensitive_mention()
    {
        var hits = UnlinkedMentions.Find("Alpha", Array.Empty<string>(),
            Notes(("b.md", "we discussed alpha today")));
        var m = Assert.Single(hits);
        Assert.Equal("b.md", m.SourceNotePath);
        Assert.Equal(13, m.MatchStart);
        Assert.Equal(5, m.MatchLength);
    }

    [Fact]
    public void Ignores_existing_links_code_and_substrings()
    {
        var hits = UnlinkedMentions.Find("Alpha", Array.Empty<string>(), Notes(
            ("b.md", "[[Alpha]] is linked, `alpha` in code, and alphabet is a substring")));
        Assert.Empty(hits);
    }

    [Fact]
    public void Matches_an_alias_too()
    {
        var hits = UnlinkedMentions.Find("Alpha", new[] { "A1" }, Notes(("b.md", "ref to A1 here")));
        Assert.Single(hits);
    }

    [Fact]
    public void LinkAll_wraps_each_mention_shortest_offset_last()
    {
        const string text = "alpha and alpha";
        var hits = UnlinkedMentions.Find("Alpha", Array.Empty<string>(), Notes(("b.md", text)));
        string linked = MentionLinker.LinkAll(text, hits, "Alpha");
        Assert.Equal("[[Alpha|alpha]] and [[Alpha|alpha]]", linked);
    }
}
