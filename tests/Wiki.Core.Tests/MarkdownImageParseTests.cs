// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Parsing;

namespace Wiki.Core.Tests;

public class MarkdownImageParseTests
{
    private static readonly MarkdigMarkdownParser Parser = new();

    [Fact]
    public void Local_markdown_image_becomes_an_embed_link()
    {
        var ast = Parser.Parse("text\n![alt](pics/cat.png)\nmore");
        var img = ast.Links.SingleOrDefault(l => l.IsEmbed && l.Target == "pics/cat.png");
        Assert.NotNull(img);
    }

    [Fact]
    public void Remote_and_non_image_markdown_links_are_not_embeds()
    {
        var ast = Parser.Parse("![a](https://x.com/y.png) and [text](notes/a.md) and ![b](a.md)");
        Assert.DoesNotContain(ast.Links, l => l.Target.StartsWith("http"));
        Assert.DoesNotContain(ast.Links, l => l.IsEmbed && l.Target == "a.md");
    }

    [Fact]
    public void Wiki_embed_still_parses()
    {
        var ast = Parser.Parse("![[cat.png]]");
        Assert.Contains(ast.Links, l => l.IsEmbed && l.Target == "cat.png");
    }
}
