// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Editor;
using Xunit;

namespace Wiki.Editor.Tests;

public class LinkCompletionTests
{
    // ---- Detect ----

    [Fact]
    public void Detect_an_open_link_returns_the_query()
    {
        var ctx = LinkCompletion.Detect("see [[alp", 9);
        Assert.NotNull(ctx);
        Assert.Equal(6, ctx!.Value.QueryStart);
        Assert.Equal("alp", ctx.Value.Query);
        Assert.Equal(-1, ctx.Value.CloseStart);
    }

    [Fact]
    public void Detect_inside_a_closed_link_reports_the_close()
    {
        // caret after "No" inside [[Note]]
        var ctx = LinkCompletion.Detect("x [[Note]] y", 6);
        Assert.NotNull(ctx);
        Assert.Equal(4, ctx!.Value.QueryStart);
        Assert.Equal("No", ctx.Value.Query);
        Assert.Equal(8, ctx.Value.CloseStart);
    }

    [Fact]
    public void Detect_an_embed_works_like_a_link()
    {
        var ctx = LinkCompletion.Detect("![[pi", 5);
        Assert.NotNull(ctx);
        Assert.Equal("pi", ctx!.Value.Query);
    }

    [Theory]
    [InlineData("no link here", 7)]
    [InlineData("done [[x]] after", 12)]   // caret past a closed link
    [InlineData("", 0)]
    public void Detect_returns_null_outside_a_link(string text, int caret)
        => Assert.Null(LinkCompletion.Detect(text, caret));

    [Fact]
    public void Detect_does_not_cross_lines()
        => Assert.Null(LinkCompletion.Detect("[[open\nnext", 9));

    // ---- Candidates ----

    [Fact]
    public void Candidates_rank_prefix_over_substring()
    {
        var notes = new[] { "misc/Balpha.md", "Alpha.md", "sub/Alphabet.md" };
        var c = LinkCompletion.Candidates(notes, "alp");
        Assert.Equal(new[] { "Alpha", "Alphabet", "Balpha" }, c);
    }

    [Fact]
    public void Candidates_empty_query_lists_alphabetically_up_to_the_limit()
    {
        var notes = new[] { "c.md", "a.md", "b.md" };
        var c = LinkCompletion.Candidates(notes, "", limit: 2);
        Assert.Equal(new[] { "a", "b" }, c);
    }

    [Fact]
    public void Candidates_without_a_match_are_empty()
        => Assert.Empty(LinkCompletion.Candidates(new[] { "a.md" }, "zzz"));

    // ---- Commit ----

    [Fact]
    public void Commit_an_open_link_inserts_the_title_and_closes()
    {
        var ctx = LinkCompletion.Detect("see [[alp", 9)!.Value;
        var r = LinkCompletion.Commit("see [[alp", ctx, 9, "Alpha");
        Assert.Equal("see [[Alpha]]", r.Text);
        Assert.Equal(13, r.SelStart);
    }

    [Fact]
    public void Commit_inside_a_closed_link_replaces_the_whole_target()
    {
        var ctx = LinkCompletion.Detect("x [[No]] y", 6)!.Value;
        var r = LinkCompletion.Commit("x [[No]] y", ctx, 6, "Note");
        Assert.Equal("x [[Note]] y", r.Text);
        Assert.Equal(10, r.SelStart);   // just past the ]]
    }

    [Fact]
    public void Commit_keeps_text_after_the_caret_in_an_open_link()
    {
        var ctx = LinkCompletion.Detect("[[alp trailing", 5)!.Value;
        var r = LinkCompletion.Commit("[[alp trailing", ctx, 5, "Alpha");
        Assert.Equal("[[Alpha]] trailing", r.Text);
    }
}
