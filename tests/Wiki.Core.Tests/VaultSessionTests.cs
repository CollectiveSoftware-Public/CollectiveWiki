// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Indexing;
using Wiki.Core.Parsing;
using Wiki.Core.Search;
using Wiki.Core.Workspace;
using Xunit;

namespace Wiki.Core.Tests;

public class VaultSessionTests
{
    private static VaultSession New(out InMemoryVaultFileSystem fs)
    {
        fs = new InMemoryVaultFileSystem(new Dictionary<string, string>
        {
            ["Home.md"] = "# Home\nlinks to [[Ideas]]\n",
            ["Ideas.md"] = "# Ideas\n",
        });
        var parser = new MarkdigMarkdownParser();
        var resolver = new LinkResolver(fs, parser);
        var index = new WikiIndex(fs, parser, resolver, new InMemoryFtsIndex());
        index.Rebuild();
        return new VaultSession(fs, index, resolver);
    }

    [Fact]
    public void Notes_lists_markdown_files()
    {
        var s = New(out _);
        Assert.Equal(new[] { "Home.md", "Ideas.md" }, s.Notes());
    }

    [Fact]
    public void Read_returns_note_content()
    {
        var s = New(out _);
        Assert.Contains("# Home", s.Read("Home.md"));
    }

    [Fact]
    public void Save_persists_and_reindexes()
    {
        var s = New(out var fs);
        s.Save("Ideas.md", "# Ideas\nnow links [[Home]]\n");
        Assert.Contains("[[Home]]", fs.ReadAllText("Ideas.md"));
        // backlinks of Home now include Ideas (index was updated)
        Assert.Contains(s.Backlinks("Home.md"), b => b.FromNote == "Ideas.md");
    }

    [Fact]
    public void CreateNote_makes_a_unique_md_file()
    {
        var s = New(out var fs);
        var path = s.CreateNote("Home");      // "Home.md" exists -> disambiguate
        Assert.True(fs.Exists(path));
        Assert.NotEqual("Home.md", path);
        Assert.Contains(path, s.Notes());
    }

    [Fact]
    public void Search_passes_through_to_the_index()
    {
        var s = New(out _);
        var hits = s.Search("Ideas");
        Assert.Contains(hits, h => h.NotePath == "Ideas.md");   // the head reaches the tf·idf+title index
    }

    [Fact]
    public void ResolveOrCreateTarget_returns_existing_when_resolved()
    {
        var s = New(out _);
        Assert.Equal("Ideas.md", s.ResolveOrCreateTarget("Ideas"));
    }

    [Fact]
    public void ResolveOrCreateTarget_creates_when_unresolved()
    {
        var s = New(out var fs);
        var path = s.ResolveOrCreateTarget("Brand New");
        Assert.True(fs.Exists(path));
        Assert.Equal("Brand New.md", path);
    }

    [Fact]
    public void ResolveAssetPath_finds_image_by_basename_and_by_path()
    {
        var fs = new InMemoryVaultFileSystem(new Dictionary<string, string>
        {
            ["Home.md"] = "![[pic.png]]",
            ["Files/Generated/pic.png"] = "binary",
        });
        var parser = new MarkdigMarkdownParser();
        var resolver = new LinkResolver(fs, parser);
        var index = new WikiIndex(fs, parser, resolver, new InMemoryFtsIndex());
        index.Rebuild();
        var s = new VaultSession(fs, index, resolver);

        Assert.Equal("Files/Generated/pic.png", s.ResolveAssetPath("pic.png"));            // by bare name
        Assert.Equal("Files/Generated/pic.png", s.ResolveAssetPath("Files/Generated/pic.png")); // literal path
        Assert.Null(s.ResolveAssetPath("missing.png"));
    }

    [Fact]
    public void RenameNote_returns_new_path_and_moves_the_note()
    {
        var s = New(out var fs);
        var newPath = s.RenameNote("Ideas.md", "Concepts");
        Assert.Equal("Concepts.md", newPath);
        Assert.False(fs.Exists("Ideas.md"));
        Assert.DoesNotContain("Ideas.md", s.Notes());
        Assert.Contains("Concepts.md", s.Notes());
    }

    [Fact]
    public void RenameNote_repoints_inbound_wikilinks()
    {
        var s = New(out var fs);   // Home.md links to [[Ideas]]
        s.RenameNote("Ideas.md", "Concepts");
        Assert.Contains("[[Concepts]]", fs.ReadAllText("Home.md"));
        Assert.DoesNotContain("[[Ideas]]", fs.ReadAllText("Home.md"));
        Assert.Contains(s.Backlinks("Concepts.md"), b => b.FromNote == "Home.md");
    }

    [Fact]
    public void RenameNote_rewrites_multiple_links_in_one_note_back_to_front()
    {
        var fs = new InMemoryVaultFileSystem(new Dictionary<string, string>
        {
            ["Refs.md"] = "See [[Ideas]] and again [[Ideas]] here.\n",
            ["Ideas.md"] = "# Ideas\n",
        });
        var parser = new MarkdigMarkdownParser();
        var resolver = new LinkResolver(fs, parser);
        var index = new WikiIndex(fs, parser, resolver, new InMemoryFtsIndex());
        index.Rebuild();
        var s = new VaultSession(fs, index, resolver);

        s.RenameNote("Ideas.md", "Concepts");
        Assert.Equal("See [[Concepts]] and again [[Concepts]] here.\n", fs.ReadAllText("Refs.md"));
    }

    [Fact]
    public void RenameNote_suffixes_on_name_collision()
    {
        var s = New(out var fs);   // Home.md, Ideas.md
        var newPath = s.RenameNote("Ideas.md", "Home");   // "Home.md" already exists
        Assert.Equal("Home 2.md", newPath);
        Assert.True(fs.Exists("Home 2.md"));
        Assert.True(fs.Exists("Home.md"));   // the original Home is untouched
    }

    [Fact]
    public void RenameNote_same_name_is_a_noop()
    {
        var s = New(out var fs);
        Assert.Equal("Ideas.md", s.RenameNote("Ideas.md", "Ideas"));
        Assert.True(fs.Exists("Ideas.md"));
    }

    [Fact]
    public void RenameNote_blank_title_throws()
    {
        var s = New(out _);
        Assert.Throws<ArgumentException>(() => s.RenameNote("Ideas.md", "   "));
    }

    [Fact]
    public void DeleteNote_removes_the_note_from_notes_and_search()
    {
        var s = New(out var fs);
        s.DeleteNote("Ideas.md");
        Assert.False(fs.Exists("Ideas.md"));
        Assert.DoesNotContain("Ideas.md", s.Notes());
        Assert.DoesNotContain(s.Search("Ideas"), h => h.NotePath == "Ideas.md");
    }

    [Fact]
    public void DeleteNote_missing_path_does_not_throw()
    {
        var s = New(out _);
        s.DeleteNote("Nope.md");
        Assert.DoesNotContain("Nope.md", s.Notes());
    }

    [Fact]
    public void ResolveAssetPath_prefers_shortest_path_over_backup_sibling_folder()
    {
        // A live asset and a stale copy in a sibling "*.backup-*" folder share a basename. Because '.'
        // (0x2E) sorts before '/' (0x2F), the ordinal-sorted enumeration lists the backup FIRST, so a
        // naive "paths[0]" picks the stale copy. a bare-name embed resolves to the SHORTEST matching path;
        // we must too, i.e. the canonical Files/Portraits/... file, not the backup.
        var fs = new InMemoryVaultFileSystem(new Dictionary<string, string>
        {
            ["People/The Acrobat.md"] = "![[Portrait - The Acrobat.png]]",
            ["Files/Portraits/Portrait - The Acrobat.png"] = "current",
            ["Files/Portraits.backup-20260623-100549/Portrait - The Acrobat.png"] = "stale",
        });
        var parser = new MarkdigMarkdownParser();
        var resolver = new LinkResolver(fs, parser);
        var index = new WikiIndex(fs, parser, resolver, new InMemoryFtsIndex());
        index.Rebuild();
        var s = new VaultSession(fs, index, resolver);

        Assert.Equal("Files/Portraits/Portrait - The Acrobat.png",
            s.ResolveAssetPath("Portrait - The Acrobat.png"));
    }
}
