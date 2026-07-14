// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Indexing;
using Wiki.Core.Parsing;
using Wiki.Core.Search;
using Wiki.Core.Vault;

namespace Wiki.Core.Tests;

public class IncrementalIndexTests
{
    private static (WikiIndex Index, InMemoryVaultFileSystem Fs, FakeVaultWatcher Watcher) Setup()
    {
        var fs = new InMemoryVaultFileSystem(new Dictionary<string, string>
        {
            ["A.md"] = "links to [[B]] #x",
            ["B.md"] = "i am b",
        });
        var index = new WikiIndex(fs, new MarkdigMarkdownParser(), new LinkResolver(fs), new InMemoryFtsIndex());
        index.Rebuild();
        var watcher = new FakeVaultWatcher();
        watcher.Changed += (_, c) => index.Apply(c);
        return (index, fs, watcher);
    }

    [Fact]
    public void Added_note_appears_in_index_and_search()
    {
        var (index, fs, watcher) = Setup();
        fs.WriteAllText("C.md", "fresh note about widgets [[A]]");
        watcher.Emit(new VaultChange(VaultChangeKind.Added, "C.md", null));

        Assert.Contains("C.md", index.AllNotes());
        Assert.Contains(index.BacklinksOf("A.md"), b => b.FromNote == "C.md");
        Assert.Contains(index.Search("widgets"), h => h.NotePath == "C.md");
    }

    [Fact]
    public void Modified_note_updates_links_and_fts()
    {
        var (index, fs, watcher) = Setup();
        fs.WriteAllText("A.md", "now points to [[B]] and mentions zebra");
        watcher.Emit(new VaultChange(VaultChangeKind.Modified, "A.md", null));

        Assert.Contains(index.Search("zebra"), h => h.NotePath == "A.md");
        Assert.DoesNotContain("A.md", index.NotesWithTag("x"));   // the #x tag was removed
    }

    [Fact]
    public void Deleted_note_is_removed_everywhere()
    {
        var (index, fs, watcher) = Setup();
        watcher.Emit(new VaultChange(VaultChangeKind.Deleted, "A.md", null));

        Assert.DoesNotContain("A.md", index.AllNotes());
        Assert.Empty(index.BacklinksOf("B.md"));                 // A was B's only backlink
        Assert.Empty(index.Search("links"));
    }

    [Fact]
    public void Renamed_note_moves_its_entry()
    {
        var (index, fs, watcher) = Setup();
        fs.Rename("B.md", "Beta.md");
        watcher.Emit(new VaultChange(VaultChangeKind.Renamed, "Beta.md", "B.md"));

        Assert.Contains("Beta.md", index.AllNotes());
        Assert.DoesNotContain("B.md", index.AllNotes());
        Assert.Contains(index.Search("i am b"), h => h.NotePath == "Beta.md");
    }
}
