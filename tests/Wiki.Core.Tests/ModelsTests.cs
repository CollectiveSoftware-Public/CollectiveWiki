// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Models;

namespace Wiki.Core.Tests;

public class ModelsTests
{
    [Fact]
    public void WikiLink_display_text_prefers_alias_then_target()
    {
        Assert.Equal("Home", new WikiLink("Home", null, null, false, 0, 8).DisplayText);
        Assert.Equal("start", new WikiLink("Home", null, "start", false, 0, 17).DisplayText);
    }

    [Fact]
    public void Records_have_value_equality()
    {
        Assert.Equal(new TagRef("a/b", 1, 5), new TagRef("a/b", 1, 5));
        Assert.NotEqual(new TagRef("a/b", 1, 5), new TagRef("a/c", 1, 5));
    }
}
