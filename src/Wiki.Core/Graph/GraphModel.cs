// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Indexing;

namespace Wiki.Core.Graph;

/// <summary>An immutable link graph built from the index. Adjacency is precomputed undirected for fast
/// neighborhood queries; edges retain their original direction.</summary>
public sealed class GraphModel : IGraphModel
{
    private readonly Dictionary<string, HashSet<string>> _adjacency;

    public IReadOnlyList<GraphNode> Nodes { get; }
    public IReadOnlyList<GraphEdge> Edges { get; }

    private GraphModel(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges)
    {
        Nodes = nodes;
        Edges = edges;
        _adjacency = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var n in nodes) _adjacency[n.NotePath] = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in edges)
        {
            if (_adjacency.TryGetValue(e.FromNote, out var a)) a.Add(e.ToNote);
            if (_adjacency.TryGetValue(e.ToNote, out var b)) b.Add(e.FromNote);
        }
    }

    public static GraphModel Build(IWikiIndex index, ILinkResolver resolver)
    {
        var notes = index.AllNotes();
        var nodeSet = new HashSet<string>(notes, StringComparer.Ordinal);
        var nodes = notes.Select(n => new GraphNode(n)).ToList();
        var edges = new List<GraphEdge>();
        foreach (var from in notes)
            foreach (var link in index.LinksOf(from))
            {
                var to = resolver.Resolve(link.Target);
                if (to is not null && nodeSet.Contains(to) && to != from)
                    edges.Add(new GraphEdge(from, to));
            }
        return new GraphModel(nodes, edges);
    }

    public IReadOnlyList<string> Neighbors(string notePath)
        => _adjacency.TryGetValue(notePath, out var set)
            ? set.OrderBy(p => p, StringComparer.Ordinal).ToList()
            : Array.Empty<string>();

    public IGraphModel Neighborhood(string center, int depth)
    {
        var keep = new HashSet<string>(StringComparer.Ordinal);
        if (_adjacency.ContainsKey(center))
        {
            keep.Add(center);
            var frontier = new HashSet<string>(StringComparer.Ordinal) { center };
            for (int d = 0; d < depth; d++)
            {
                var next = new HashSet<string>(StringComparer.Ordinal);
                foreach (var node in frontier)
                    foreach (var nb in _adjacency[node])
                        if (keep.Add(nb)) next.Add(nb);
                frontier = next;
            }
        }
        var nodes = Nodes.Where(n => keep.Contains(n.NotePath)).ToList();
        var edges = Edges.Where(e => keep.Contains(e.FromNote) && keep.Contains(e.ToNote)).ToList();
        return new GraphModel(nodes, edges);
    }
}
