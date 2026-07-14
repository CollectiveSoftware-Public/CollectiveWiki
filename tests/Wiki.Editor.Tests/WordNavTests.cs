// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Editor;
using Xunit;

namespace Wiki.Editor.Tests;

public class WordNavTests
{
    [Theory]
    [InlineData("alpha beta", 10, 6)]   // from the end of "beta" to its start
    [InlineData("alpha beta", 6, 0)]    // from the start of "beta" over the space to "alpha"
    [InlineData("a-b", 3, 2)]           // punctuation is its own class
    [InlineData("alpha", 0, 0)]         // clamped at 0
    public void Prev_boundary(string text, int from, int expected)
        => Assert.Equal(expected, WordNav.PrevBoundary(text, from));

    [Theory]
    [InlineData("alpha beta", 0, 6)]    // skips "alpha" + the space to the next word start
    [InlineData("alpha beta", 6, 10)]   // to the end of the text
    [InlineData("alpha", 5, 5)]         // clamped at the end
    public void Next_boundary(string text, int from, int expected)
        => Assert.Equal(expected, WordNav.NextBoundary(text, from));

    [Fact]
    public void Word_at_selects_the_word_around_the_offset()
    {
        var (s, e) = WordNav.WordAt("alpha beta", 7);
        Assert.Equal(6, s);
        Assert.Equal(10, e);
    }

    [Fact]
    public void Word_at_on_whitespace_selects_the_run()
    {
        var (s, e) = WordNav.WordAt("a  b", 1);
        Assert.Equal(1, s);
        Assert.Equal(3, e);
    }

    [Fact]
    public void Word_at_on_empty_text_is_empty()
        => Assert.Equal((0, 0), WordNav.WordAt("", 0));
}
