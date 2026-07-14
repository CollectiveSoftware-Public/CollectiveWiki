// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Linq;
using Wiki.Core.Workspace;
using Xunit;

namespace Wiki.Core.Tests;

public class VaultTreeSearchTests
{
    // Mirrors VaultTreeBuilder output: folders carry Children (NotePath null), notes carry NotePath.
    private static VaultNode Note(string name, string path) => new(name, path, Array.Empty<VaultNode>());
    private static VaultNode Folder(string name, params VaultNode[] kids) => new(name, null, kids);

    [Fact]
    public void PathTo_FindsANestedNote_ReturningTheAncestorChain()
    {
        var tree = new[] { Folder("sub", Note("Alpha", "sub/Alpha.md")), Note("Root", "Root.md") };
        var chain = VaultTreeSearch.PathTo(tree, "sub/Alpha.md");
        Assert.Equal(new[] { "sub", "Alpha" }, chain.Select(n => n.Name).ToArray());
    }

    [Fact]
    public void PathTo_TopLevelNote_ReturnsJustTheNote()
    {
        var tree = new[] { Note("Root", "Root.md") };
        var chain = VaultTreeSearch.PathTo(tree, "Root.md");
        Assert.Single(chain);
        Assert.Equal("Root", chain[0].Name);
    }

    [Fact]
    public void PathTo_Missing_ReturnsEmpty()
        => Assert.Empty(VaultTreeSearch.PathTo(new[] { Note("A", "A.md") }, "nope.md"));
}
