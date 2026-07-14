// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Workspace;
using Xunit;

namespace Wiki.Core.Tests;

public class VaultFolderOpsTests
{
    [Fact]
    public void CreateDirectory_then_EnumerateFolders_lists_the_folder_and_its_parents()
    {
        var fs = new InMemoryVaultFileSystem();
        fs.CreateDirectory("a/b");
        Assert.Contains("a", fs.EnumerateFolders());
        Assert.Contains("a/b", fs.EnumerateFolders());
    }

    [Fact]
    public void EnumerateFolders_includes_folders_implied_by_note_paths()
    {
        var fs = new InMemoryVaultFileSystem(new Dictionary<string, string> { ["x/y/n.md"] = "# n\n" });
        Assert.Contains("x", fs.EnumerateFolders());
        Assert.Contains("x/y", fs.EnumerateFolders());
    }

    [Fact]
    public void DeleteDirectory_removes_the_folder_and_everything_under_it()
    {
        var fs = new InMemoryVaultFileSystem(new Dictionary<string, string> { ["a/b/n.md"] = "# n\n" });
        fs.CreateDirectory("a/empty");
        fs.DeleteDirectory("a");
        Assert.DoesNotContain("a", fs.EnumerateFolders());
        Assert.DoesNotContain("a/b", fs.EnumerateFolders());
        Assert.False(fs.Exists("a/b/n.md"));
    }

    [Fact]
    public void Tree_builder_shows_empty_folders_from_the_folder_list()
    {
        var tree = VaultTreeBuilder.Build(new[] { "a/n.md" }, new[] { "a", "empty" });
        Assert.Contains(tree, n => n is { IsNote: false, Name: "empty" });   // the empty folder appears
        Assert.Contains(tree, n => n is { IsNote: false, Name: "a" });
    }
}
