// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Indexing;
using Wiki.Core.Parsing;

namespace Wiki.Core.Tests;

public class LinkResolverAliasTests
{
    private static LinkResolver WithAliases(InMemoryVaultFileSystem fs)
        => new(fs, new MarkdigMarkdownParser());

    [Fact]
    public void Resolves_a_link_via_a_frontmatter_alias()
    {
        var fs = new InMemoryVaultFileSystem(new Dictionary<string, string>
        {
            ["Target.md"] = "---\naliases: TT, The Target\n---\n# Target",
        });
        var r = WithAliases(fs);
        Assert.Equal("Target.md", r.Resolve("TT"));
        Assert.Equal("Target.md", r.Resolve("The Target"));
        Assert.Equal(LinkResolution.Resolved, r.Classify("the target", out var p));   // case-insensitive
        Assert.Equal("Target.md", p);
    }

    [Fact]
    public void Ambiguous_alias_resolves_to_nothing()
    {
        var fs = new InMemoryVaultFileSystem(new Dictionary<string, string>
        {
            ["A.md"] = "---\naliases: Dup\n---\n",
            ["B.md"] = "---\naliases: Dup\n---\n",
        });
        Assert.Equal(LinkResolution.Ambiguous, WithAliases(fs).Classify("Dup", out var p));
        Assert.Null(p);
    }

    [Fact]
    public void Normalizes_backslashes_and_leading_dot_slash()
    {
        var fs = new InMemoryVaultFileSystem(new Dictionary<string, string> { ["sub/Note.md"] = "" });
        var r = WithAliases(fs);
        Assert.Equal("sub/Note.md", r.Resolve(@"sub\Note"));
        Assert.Equal("sub/Note.md", r.Resolve("./sub/Note"));
    }

    [Fact]
    public void Without_a_parser_aliases_are_ignored_but_paths_still_resolve()
    {
        var fs = new InMemoryVaultFileSystem(new Dictionary<string, string>
        {
            ["Target.md"] = "---\naliases: TT\n---\n",
        });
        var r = new LinkResolver(fs);                    // no parser → no alias support
        Assert.Null(r.Resolve("TT"));
        Assert.Equal("Target.md", r.Resolve("Target"));  // path resolution unaffected
    }

    [Fact]
    public void Invalidate_picks_up_a_newly_added_alias()
    {
        var fs = new InMemoryVaultFileSystem();
        var r = WithAliases(fs);
        Assert.Null(r.Resolve("TT"));                    // builds (empty) alias map
        fs.WriteAllText("Target.md", "---\naliases: TT\n---\n");
        r.InvalidateAliases();
        Assert.Equal("Target.md", r.Resolve("TT"));
    }
}
