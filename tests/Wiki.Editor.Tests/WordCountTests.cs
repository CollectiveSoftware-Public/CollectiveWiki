// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Editor;
using Xunit;

namespace Wiki.Editor.Tests;

public class WordCountTests
{
    [Theory]
    [InlineData("", 0, 0)]
    [InlineData("one", 1, 3)]
    [InlineData("one two\nthree", 3, 13)]
    [InlineData("  spaced   out  ", 2, 16)]
    [InlineData("tabs\tcount\ttoo", 3, 14)]
    public void Count(string text, int words, int chars)
        => Assert.Equal((words, chars), WordCount.Count(text));
}
