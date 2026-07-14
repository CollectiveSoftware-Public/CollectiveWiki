// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Workspace;

/// <summary>Pure tree lookup for "reveal the active note in the Files tree": returns the chain of
/// nodes from a root down to the node whose <c>NotePath</c> matches (the ancestors the head must
/// expand, plus the target it selects). Empty when the note isn't in the tree. Avalonia-free.</summary>
public static class VaultTreeSearch
{
    public static IReadOnlyList<VaultNode> PathTo(IEnumerable<VaultNode> roots, string notePath)
    {
        var acc = new List<VaultNode>();
        return Walk(roots, notePath, acc) ? acc : Array.Empty<VaultNode>();
    }

    private static bool Walk(IEnumerable<VaultNode> nodes, string notePath, List<VaultNode> acc)
    {
        foreach (var n in nodes)
        {
            acc.Add(n);
            if (n.IsNote && string.Equals(n.NotePath, notePath, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!n.IsNote && n.Children is { Count: > 0 } && Walk(n.Children, notePath, acc))
                return true;
            acc.RemoveAt(acc.Count - 1);
        }
        return false;
    }
}
