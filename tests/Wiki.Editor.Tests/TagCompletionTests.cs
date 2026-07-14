// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Editor;
using Xunit;

namespace Wiki.Editor.Tests;

public class TagCompletionTests
{
    [Fact]
    public void Detect_AfterHashAtWordBoundary_ReturnsQuery()
    {
        var ctx = TagCompletion.Detect("intro #pro", 10);
        Assert.NotNull(ctx);
        Assert.Equal(7, ctx!.Value.QueryStart);   // just past the '#'
        Assert.Equal("pro", ctx.Value.Query);
    }

    [Fact]
    public void Detect_NestedTagChars_AreIncluded()
    {
        var ctx = TagCompletion.Detect("#area/sub", 9);
        Assert.Equal("area/sub", ctx!.Value.Query);
    }

    [Theory]
    [InlineData("no hash here", 5)]      // no '#'
    [InlineData("a#b", 3)]               // '#' not at a word boundary (letter before)
    [InlineData("# heading", 2)]         // ATX heading — space isn't a tag char, so the run ends at '#'
    public void Detect_ReturnsNullWhenNotATag(string text, int caret)
        => Assert.Null(TagCompletion.Detect(text, caret));

    [Fact]
    public void Candidates_RanksLikeQuickSwitcher()
    {
        var tags = new[] { "project", "process", "misc" };
        var c = TagCompletion.Candidates(tags, "pro");
        Assert.Equal(new[] { "process", "project" }, c);   // prefix matches, alphabetical tiebreak
    }

    [Fact]
    public void Commit_ReplacesTheTypedQueryWithTheTag()
    {
        var ctx = TagCompletion.Detect("see #pro", 8)!.Value;
        var r = TagCompletion.Commit("see #pro", ctx, 8, "process");
        Assert.Equal("see #process", r.Text);
        Assert.Equal(12, r.SelStart);
    }
}
