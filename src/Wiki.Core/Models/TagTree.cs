// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Models;

/// <summary>One node of the nested-tag tree: a <c>/</c>-delimited tag segment, its full path, the count of
/// notes tagged exactly this path (<see cref="OwnCount"/>), the count including descendants
/// (<see cref="TotalCount"/>), and its children.</summary>
public sealed record TagTreeNode(
    string Segment, string FullPath, int OwnCount, int TotalCount, IReadOnlyList<TagTreeNode> Children);

/// <summary>Pure: turns the flat (tag, note-count) list into a nested tree by splitting each tag on
/// <c>/</c>. Alphabetical per level; <see cref="TagTreeNode.TotalCount"/> aggregates own + descendants.</summary>
public static class TagTree
{
    public static IReadOnlyList<TagTreeNode> Build(IReadOnlyList<(string Tag, int Count)> tags)
    {
        var root = new Node("", "");
        foreach (var (tag, count) in tags)
        {
            var node = root; string acc = "";
            foreach (var seg in tag.Split('/'))
            {
                acc = acc.Length == 0 ? seg : acc + "/" + seg;
                node = node.Child(seg, acc);
            }
            node.Own += count;
        }
        return root.Freeze().Children;
    }

    private sealed class Node
    {
        public readonly string Segment, Full; public int Own;
        public readonly Dictionary<string, Node> Kids = new(StringComparer.OrdinalIgnoreCase);
        public Node(string s, string f) { Segment = s; Full = f; }
        public Node Child(string seg, string full)
        {
            if (!Kids.TryGetValue(seg, out var n)) Kids[seg] = n = new Node(seg, full);
            return n;
        }
        public TagTreeNode Freeze()
        {
            var kids = Kids.Values.OrderBy(k => k.Segment, StringComparer.OrdinalIgnoreCase)
                .Select(k => k.Freeze()).ToList();
            return new TagTreeNode(Segment, Full, Own, Own + kids.Sum(k => k.TotalCount), kids);
        }
    }
}
