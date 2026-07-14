// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Graph;

namespace Wiki.Core.Tests;

public class GraphLayoutTests
{
    // Minimal hand-built graph so the test needs no vault/index.
    private sealed class FakeGraph : IGraphModel
    {
        public IReadOnlyList<GraphNode> Nodes { get; init; } = System.Array.Empty<GraphNode>();
        public IReadOnlyList<GraphEdge> Edges { get; init; } = System.Array.Empty<GraphEdge>();
        public IReadOnlyList<string> Neighbors(string notePath) => System.Array.Empty<string>();
        public IGraphModel Neighborhood(string center, int depth) => this;
    }

    private static readonly FakeGraph G = new()
    {
        Nodes = new[] { new GraphNode("a.md"), new GraphNode("b.md"), new GraphNode("c.md") },
        Edges = new[] { new GraphEdge("a.md", "b.md") },
    };

    [Fact]
    public void Produces_one_position_per_node_inside_the_unit_box()
    {
        var pos = GraphLayout.Compute(G);
        Assert.Equal(3, pos.Count);
        Assert.All(pos, p => { Assert.InRange(p.X, 0, 1); Assert.InRange(p.Y, 0, 1); });
        Assert.Equal(new[] { "a.md", "b.md", "c.md" }, pos.Select(p => p.NotePath).OrderBy(x => x));
    }

    [Fact]
    public void Is_deterministic()
    {
        var a = GraphLayout.Compute(G);
        var b = GraphLayout.Compute(G);
        Assert.Equal(a.Select(p => (p.NotePath, p.X, p.Y)), b.Select(p => (p.NotePath, p.X, p.Y)));
    }

    [Fact]
    public void Empty_graph_yields_no_positions()
        => Assert.Empty(GraphLayout.Compute(new FakeGraph()));
}
