// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Workspace;

/// <summary>One node in the vault's folder tree: a folder (<see cref="NotePath"/> null, with
/// <see cref="Children"/>) or a note leaf (<see cref="NotePath"/> set to its '/'-relative path).
/// <see cref="Name"/> is the folder name, or the note's file name without the <c>.md</c> extension.</summary>
public sealed record VaultNode(string Name, string? NotePath, IReadOnlyList<VaultNode> Children)
{
    public bool IsNote => NotePath is not null;

    /// <summary>The '/'-joined path of a <em>folder</em> node (e.g. <c>Machines/Sub</c>), used to remember
    /// its expansion state across tree rebuilds; <c>null</c> for note leaves.</summary>
    public string? FolderPath { get; init; }
}

/// <summary>Pure: turns a flat list of '/'-relative note paths into a nested folder tree (like a file
/// explorer). Folders sort before notes; both alphabetically, case-insensitive. UI-free + unit-tested.</summary>
public static class VaultTreeBuilder
{
    public static IReadOnlyList<VaultNode> Build(IReadOnlyList<string> notePaths)
        => Build(notePaths, Array.Empty<string>());

    /// <summary>As <see cref="Build(IReadOnlyList{string})"/>, but also seeds <paramref name="folderPaths"/>
    /// so <em>empty</em> folders (no notes yet) still appear in the tree.</summary>
    public static IReadOnlyList<VaultNode> Build(IReadOnlyList<string> notePaths, IReadOnlyList<string> folderPaths)
    {
        var root = new Dir();
        foreach (var folder in folderPaths)
        {
            var dir = root;
            foreach (var part in folder.Split('/'))
                if (part.Length > 0) dir = dir.Folder(part);
        }
        foreach (var path in notePaths)
        {
            var parts = path.Split('/');
            var dir = root;
            for (int i = 0; i < parts.Length - 1; i++)
                dir = dir.Folder(parts[i]);
            dir.Notes.Add((StripMd(parts[^1]), path));
        }
        return root.ToNodes("");
    }

    /// <summary>Ordinal sequence equality of two note-path lists — the cheap "did the vault's note set
    /// actually change?" check that lets the head skip a tree rebuild (which would collapse expansion) on a
    /// save or a navigation that didn't add/remove/rename a note. False when <paramref name="previous"/> is
    /// null (no prior build to compare against).</summary>
    public static bool SameNotes(IReadOnlyList<string>? previous, IReadOnlyList<string> current)
    {
        if (previous is null || previous.Count != current.Count) return false;
        for (int i = 0; i < current.Count; i++)
            if (!string.Equals(previous[i], current[i], StringComparison.Ordinal)) return false;
        return true;
    }

    private static string StripMd(string fileName)
        => fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? fileName[..^3] : fileName;

    // Mutable builder mirror of the immutable VaultNode tree.
    private sealed class Dir
    {
        private readonly Dictionary<string, Dir> _folders = new(StringComparer.Ordinal);
        public List<(string Name, string Path)> Notes { get; } = new();

        public Dir Folder(string name)
        {
            if (!_folders.TryGetValue(name, out var d)) _folders[name] = d = new Dir();
            return d;
        }

        public IReadOnlyList<VaultNode> ToNodes(string prefix)
        {
            var nodes = new List<VaultNode>();
            foreach (var kv in _folders.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
            {
                string folderPath = prefix.Length == 0 ? kv.Key : prefix + "/" + kv.Key;
                nodes.Add(new VaultNode(kv.Key, null, kv.Value.ToNodes(folderPath)) { FolderPath = folderPath });
            }
            foreach (var (name, path) in Notes.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
                nodes.Add(new VaultNode(name, path, Array.Empty<VaultNode>()));
            return nodes;
        }
    }
}
