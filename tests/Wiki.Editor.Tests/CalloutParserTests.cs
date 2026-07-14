// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Editor;
using Xunit;

namespace Wiki.Editor.Tests;

public class CalloutParserTests
{
    [Fact]
    public void DetectHeader_WithTitle()
    {
        var c = CalloutParser.DetectHeader("> [!warning] Heads up");
        Assert.Equal("warning", c!.Type);
        Assert.Equal("Heads up", c.Title);
        Assert.Equal("amber", c.Family);
    }

    [Fact]
    public void DetectHeader_NoTitle_DefaultsToCapitalizedType()
    {
        var c = CalloutParser.DetectHeader("> [!note]");
        Assert.Equal("Note", c!.Title);
        Assert.Equal("blue", c.Family);
    }

    [Theory]
    [InlineData("> just a quote")]
    [InlineData("not a quote")]
    public void DetectHeader_PlainBlockquote_ReturnsNull(string line)
        => Assert.Null(CalloutParser.DetectHeader(line));

    [Theory]
    [InlineData("tip", "green")]
    [InlineData("danger", "red")]
    [InlineData("question", "purple")]
    [InlineData("weirdtype", "grey")]
    public void Family_MapsTypes(string type, string family)
        => Assert.Equal(family, CalloutParser.Family(type));
}
