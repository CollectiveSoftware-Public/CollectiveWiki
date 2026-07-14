// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Editor;
using Xunit;

namespace Wiki.Editor.Tests;

public class FrontmatterParserTests
{
    [Fact]
    public void Parses_leading_block_with_ordered_entries()
    {
        var fm = FrontmatterParser.Parse("---\ntype: index\nstatus: draft\n---\n# Body");
        Assert.NotNull(fm);
        Assert.Equal(0, fm!.StartLine);
        Assert.Equal(3, fm.EndLine);
        Assert.Equal(new[] { "type", "status" }, fm.Entries.Select(e => e.Key).ToArray());
        Assert.Equal("index", fm.Entries[0].Value);
        Assert.Equal("draft", fm.Entries[1].Value);
    }

    [Fact]
    public void No_frontmatter_returns_null()
        => Assert.Null(FrontmatterParser.Parse("# Body\nplain"));

    [Fact]
    public void Fence_not_at_line_zero_returns_null()
        => Assert.Null(FrontmatterParser.Parse("text\n---\nk: v\n---"));

    [Fact]
    public void Unterminated_fence_returns_null()
        => Assert.Null(FrontmatterParser.Parse("---\nk: v\nmore"));
}
