// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Parsing;

namespace Wiki.Core.Tests;

public class WikiSyntaxScannerTests
{
    private static (IReadOnlyList<Models.WikiLink> Links, IReadOnlyList<Models.TagRef> Tags) Scan(string md)
    {
        var ast = new MarkdigMarkdownParser().Parse(md);
        return (ast.Links, ast.Tags);
    }

    [Fact]
    public void Plain_wikilink()
    {
        var l = Single(Scan("See [[Home]] today.").Links);
        Assert.Equal("Home", l.Target);
        Assert.Null(l.Heading);
        Assert.Null(l.Alias);
        Assert.False(l.IsEmbed);
        Assert.Equal("[[Home]]", "See [[Home]] today.".Substring(l.SourceStart, l.SourceEnd - l.SourceStart));
    }

    [Fact]
    public void Heading_and_alias_and_embed_forms()
    {
        var h = Single(Scan("[[Note#Intro]]").Links);
        Assert.Equal("Note", h.Target); Assert.Equal("Intro", h.Heading);

        var a = Single(Scan("[[Note|click here]]").Links);
        Assert.Equal("Note", a.Target); Assert.Equal("click here", a.Alias);

        var both = Single(Scan("[[Note#Intro|click]]").Links);
        Assert.Equal("Note", both.Target); Assert.Equal("Intro", both.Heading); Assert.Equal("click", both.Alias);

        var e = Single(Scan("![[Diagram]]").Links);
        Assert.True(e.IsEmbed); Assert.Equal("Diagram", e.Target);
        Assert.Equal("![[Diagram]]", "![[Diagram]]".Substring(e.SourceStart, e.SourceEnd - e.SourceStart));
    }

    [Fact]
    public void Links_in_lists_and_quotes_are_found()
    {
        Assert.Single(Scan("- a [[One]]\n- b").Links);
        Assert.Single(Scan("> quoting [[Two]]").Links);
    }

    [Fact]
    public void Links_and_tags_inside_code_are_ignored()
    {
        Assert.Empty(Scan("`[[NotALink]]` and `#nottag`").Links);
        Assert.Empty(Scan("`[[NotALink]]` and `#nottag`").Tags);
        Assert.Empty(Scan("```\n[[NotALink]] #nottag\n```").Links);
        Assert.Empty(Scan("```\n[[NotALink]] #nottag\n```").Tags);
    }

    [Fact]
    public void Tags_simple_nested_and_word_boundary()
    {
        Assert.Equal("project", Single(Scan("a #project here").Tags).Name);
        Assert.Equal("area/work", Single(Scan("nested #area/work tag").Tags).Name);
        Assert.Empty(Scan("an email a#b is not a tag").Tags);   // mid-word # rejected
        Assert.Empty(Scan("# A Heading").Tags);                  // heading # is not a tag
    }

    [Fact]
    public void Frontmatter_fields_are_read()
    {
        var fm = new MarkdigMarkdownParser().Parse("---\ntitle: Hello\naliases: x\n---\n# Body").Frontmatter;
        Assert.Equal("Hello", fm["title"]);
        Assert.Equal("x", fm["aliases"]);
    }

    private static T Single<T>(IReadOnlyList<T> xs) { Assert.Single(xs); return xs[0]; }
}
