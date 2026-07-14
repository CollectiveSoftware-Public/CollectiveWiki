// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Graph;
using Wiki.Core.Indexing;
using Wiki.Core.Parsing;
using Wiki.Core.Search;

namespace Wiki.Core.Tests;

public class GraphModelTests
{
    private static GraphModel Build(InMemoryVaultFileSystem fs)
    {
        var resolver = new LinkResolver(fs);
        var index = new WikiIndex(fs, new MarkdigMarkdownParser(), resolver, new InMemoryFtsIndex());
        index.Rebuild();
        return GraphModel.Build(index, resolver);
    }

    private static InMemoryVaultFileSystem Vault() => new(new Dictionary<string, string>
    {
        ["A.md"] = "[[B]] and [[C]]",
        ["B.md"] = "[[C]]",
        ["C.md"] = "no links",
        ["D.md"] = "[[Ghost]]",      // unresolved link -> no edge
    });

    [Fact]
    public void Nodes_are_all_notes_edges_are_resolved_links()
    {
        var g = Build(Vault());
        Assert.Equal(4, g.Nodes.Count);
        Assert.Contains(g.Edges, e => e.FromNote == "A.md" && e.ToNote == "B.md");
        Assert.Contains(g.Edges, e => e.FromNote == "A.md" && e.ToNote == "C.md");
        Assert.Contains(g.Edges, e => e.FromNote == "B.md" && e.ToNote == "C.md");
        Assert.DoesNotContain(g.Edges, e => e.FromNote == "D.md");   // [[Ghost]] is unresolved
    }

    [Fact]
    public void Neighbors_are_undirected()
    {
        var g = Build(Vault());
        Assert.Contains("B.md", g.Neighbors("A.md"));   // outbound
        Assert.Contains("A.md", g.Neighbors("B.md"));   // inbound, same edge
    }

    [Fact]
    public void Neighborhood_limits_to_a_depth()
    {
        var g = Build(Vault());
        var d1 = g.Neighborhood("A.md", 1);
        Assert.Contains(d1.Nodes, n => n.NotePath == "A.md");
        Assert.Contains(d1.Nodes, n => n.NotePath == "B.md");
        Assert.Contains(d1.Nodes, n => n.NotePath == "C.md");
        Assert.DoesNotContain(d1.Nodes, n => n.NotePath == "D.md");  // disconnected from A
    }
}
