// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Indexing;
using Wiki.Core.Parsing;

namespace Wiki.Core.Tests;

public class LinkResolverTests
{
    [Fact]
    public void Classifies_resolved_unresolved_and_ambiguous()
    {
        var fs = new InMemoryVaultFileSystem(new Dictionary<string, string>
        {
            ["A.md"] = "[[B]]",
            ["B.md"] = "",
            ["x/Dup.md"] = "",
            ["y/Dup.md"] = "",
        });
        var r = new LinkResolver(fs);

        Assert.Equal(LinkResolution.Resolved, r.Classify("B", out var p)); Assert.Equal("B.md", p);
        Assert.Equal(LinkResolution.Unresolved, r.Classify("Ghost", out var p2)); Assert.Null(p2);
        Assert.Equal(LinkResolution.Ambiguous, r.Classify("Dup", out var p3)); Assert.Null(p3);
    }

    [Fact]
    public void Basename_resolution_updates_after_invalidate()
    {
        // The resolver caches its basename lookup so BacklinksOf doesn't rescan the whole vault per link;
        // InvalidateAliases must clear it so a newly added nested note resolves.
        var fs = new InMemoryVaultFileSystem(new Dictionary<string, string> { ["a/Note.md"] = "" });
        var r = new LinkResolver(fs);
        Assert.Equal("a/Note.md", r.Resolve("Note"));     // builds the basename cache

        fs.WriteAllText("b/Fresh.md", "");
        Assert.Null(r.Resolve("Fresh"));                  // stale cache: not yet visible
        r.InvalidateAliases();
        Assert.Equal("b/Fresh.md", r.Resolve("Fresh"));   // cache rebuilt, now resolves
    }

    [Fact]
    public void Rename_repoints_inbound_links()
    {
        var fs = new InMemoryVaultFileSystem(new Dictionary<string, string>
        {
            ["A.md"] = "see [[Old]] now",
            ["Old.md"] = "",
        });
        var index = new WikiIndex(fs, new MarkdigMarkdownParser(), new LinkResolver(fs), new Search.InMemoryFtsIndex());
        index.Rebuild();

        var rewrites = new LinkResolver(fs).ComputeRenameRewrites(index, "Old.md", "New.md");
        var rw = Assert.Single(rewrites);
        Assert.Equal("A.md", rw.NotePath);
        Assert.Equal("[[New]]", rw.NewLinkText);
        Assert.Equal("[[Old]]", "see [[Old]] now".Substring(rw.SourceStart, rw.SourceEnd - rw.SourceStart));
    }
}
