// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Indexing;
using Wiki.Core.Parsing;
using Wiki.Core.Search;
using Wiki.Core.Workspace;
using Xunit;

namespace Wiki.Core.Tests;

public class VaultSessionOrgTests
{
    private static VaultSession New(Dictionary<string, string> seed, out InMemoryVaultFileSystem fs)
    {
        fs = new InMemoryVaultFileSystem(seed);
        var parser = new MarkdigMarkdownParser();
        var resolver = new LinkResolver(fs, parser);
        var index = new WikiIndex(fs, parser, resolver, new InMemoryFtsIndex());
        index.Rebuild();
        return new VaultSession(fs, index, resolver);
    }

    [Fact]
    public void CreateNote_places_the_note_in_the_given_folder()
    {
        var s = New(new() { ["Home.md"] = "# Home\n" }, out var fs);
        string path = s.CreateNote("Note", "Projects");
        Assert.Equal("Projects/Note.md", path);
        Assert.True(fs.Exists("Projects/Note.md"));
    }

    [Fact]
    public void CreateNote_disambiguates_within_a_folder()
    {
        var s = New(new() { ["Home.md"] = "# Home\n" }, out _);
        Assert.Equal("F/Note.md", s.CreateNote("Note", "F"));
        Assert.Equal("F/Note 2.md", s.CreateNote("Note", "F"));
    }

    [Fact]
    public void CreateFolder_shows_up_as_an_empty_folder()
    {
        var s = New(new() { ["Home.md"] = "# Home\n" }, out _);
        string folder = s.CreateFolder("", "Ideas");
        Assert.Equal("Ideas", folder);
        Assert.Contains("Ideas", s.Folders());
    }

    [Fact]
    public void MoveNote_relocates_the_file_and_keeps_inbound_links_resolving()
    {
        var s = New(new()
        {
            ["A.md"] = "# A\nSee [[B]] for details.",
            ["B.md"] = "# B\n",
        }, out var fs);

        string moved = s.MoveNote("B.md", "sub");

        Assert.Equal("sub/B.md", moved);
        Assert.True(fs.Exists("sub/B.md"));
        Assert.False(fs.Exists("B.md"));
        // A's [[B]] still resolves to the moved note (basename resolution) — backlink intact.
        Assert.Contains(s.Backlinks("sub/B.md"), b => b.FromNote == "A.md");
    }

    [Fact]
    public void RenameFolder_moves_its_notes_and_preserves_links()
    {
        var s = New(new()
        {
            ["Old/n.md"] = "# n\n",
            ["A.md"] = "# A\nlink to [[n]].",
        }, out var fs);

        string renamed = s.RenameFolder("Old", "New");

        Assert.Equal("New", renamed);
        Assert.True(fs.Exists("New/n.md"));
        Assert.False(fs.Exists("Old/n.md"));
        Assert.Contains(s.Backlinks("New/n.md"), b => b.FromNote == "A.md");
    }

    [Fact]
    public void MoveFolder_into_its_own_subtree_is_a_no_op()
    {
        var s = New(new() { ["Top/n.md"] = "# n\n" }, out _);
        Assert.Equal("Top", s.MoveFolder("Top", "Top"));
    }

    [Fact]
    public void DeleteFolder_removes_the_folder_and_its_notes_from_disk_and_index()
    {
        var s = New(new()
        {
            ["Trash/a.md"] = "# a\n",
            ["Trash/b.md"] = "# b\n",
            ["Keep.md"] = "# Keep\n",
        }, out var fs);

        s.DeleteFolder("Trash");

        Assert.False(fs.Exists("Trash/a.md"));
        Assert.False(fs.Exists("Trash/b.md"));
        Assert.True(fs.Exists("Keep.md"));
        Assert.DoesNotContain("Trash", s.Folders());
    }
}
