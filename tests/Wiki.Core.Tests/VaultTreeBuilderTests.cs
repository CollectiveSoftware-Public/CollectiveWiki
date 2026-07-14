// SPDX-License-Identifier: GPL-3.0-or-later
using System.Linq;
using Wiki.Core.Workspace;
using Xunit;

namespace Wiki.Core.Tests;

public class VaultTreeBuilderTests
{
    [Fact]
    public void Build_groups_notes_into_folders_folders_first()
    {
        var nodes = VaultTreeBuilder.Build(new[]
        {
            "Atlas/Continents/Asuria.md",
            "Atlas/Chokepoints/The Sound.md",
            "Home.md",
        });

        // Top level: folder "Atlas" before note "Home" (folders sort first).
        Assert.Equal(2, nodes.Count);
        Assert.Equal("Atlas", nodes[0].Name);
        Assert.Null(nodes[0].NotePath);
        Assert.False(nodes[0].IsNote);

        Assert.Equal("Home", nodes[1].Name);
        Assert.Equal("Home.md", nodes[1].NotePath);   // leaf carries the '/'-relative path
        Assert.True(nodes[1].IsNote);

        // Atlas has two subfolders, alphabetically.
        Assert.Equal(new[] { "Chokepoints", "Continents" }, nodes[0].Children.Select(c => c.Name));

        var continents = nodes[0].Children.First(c => c.Name == "Continents");
        var leaf = Assert.Single(continents.Children);
        Assert.Equal("Asuria", leaf.Name);                          // display name strips ".md"
        Assert.Equal("Atlas/Continents/Asuria.md", leaf.NotePath);
    }

    [Fact]
    public void Build_of_empty_list_is_empty()
        => Assert.Empty(VaultTreeBuilder.Build(System.Array.Empty<string>()));

    // ---- Task 2: folder nodes carry their '/'-joined path; note leaves don't ----

    [Fact]
    public void Build_threads_folder_paths_through_every_level()
    {
        var nodes = VaultTreeBuilder.Build(new[] { "Machines/Sub/A.md", "B.md" });

        var machines = nodes.First(n => n.Name == "Machines");
        Assert.Equal("Machines", machines.FolderPath);

        var sub = Assert.Single(machines.Children, c => !c.IsNote);
        Assert.Equal("Machines/Sub", sub.FolderPath);

        var leafA = Assert.Single(sub.Children);
        Assert.Null(leafA.FolderPath);                 // note leaves have no folder path

        var noteB = nodes.First(n => n.Name == "B");
        Assert.Null(noteB.FolderPath);
    }

    // ---- Task 2: SameNotes — ordinal sequence equality, false when previous is null ----

    [Fact]
    public void SameNotes_is_true_only_for_an_identical_sequence()
    {
        var current = new[] { "A.md", "Sub/B.md" };

        Assert.True(VaultTreeBuilder.SameNotes(new[] { "A.md", "Sub/B.md" }, current));
        Assert.False(VaultTreeBuilder.SameNotes(new[] { "Sub/B.md", "A.md" }, current));   // reordered
        Assert.False(VaultTreeBuilder.SameNotes(new[] { "A.md" }, current));               // removed
        Assert.False(VaultTreeBuilder.SameNotes(new[] { "A.md", "Sub/B.md", "C.md" }, current)); // added
        Assert.False(VaultTreeBuilder.SameNotes(null, current));                           // null previous
    }
}
