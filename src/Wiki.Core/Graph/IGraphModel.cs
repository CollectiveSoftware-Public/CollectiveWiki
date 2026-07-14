// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Graph;

/// <summary>The vault link graph: notes as nodes, resolved wikilinks as edges. Edges are directed, but
/// <see cref="Neighbors"/> and <see cref="Neighborhood"/> treat them as undirected (the link graph view
/// shows both inbound and outbound connections).</summary>
public interface IGraphModel
{
    IReadOnlyList<GraphNode> Nodes { get; }
    IReadOnlyList<GraphEdge> Edges { get; }

    /// <summary>Notes directly connected to <paramref name="notePath"/> in either direction.</summary>
    IReadOnlyList<string> Neighbors(string notePath);

    /// <summary>The sub-graph of nodes within <paramref name="depth"/> undirected hops of
    /// <paramref name="center"/> (the "local graph"), and the edges among them.</summary>
    IGraphModel Neighborhood(string center, int depth);
}
