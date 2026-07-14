// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Indexing;
using Wiki.Core.Parsing;

namespace Wiki.Core.Tests;

public class WikiIndexTests
{
    private static WikiIndex BuildCorpusIndex()
    {
        var fs = InMemoryVaultFileSystem.FromDirectory(Path.Combine(AppContext.BaseDirectory, "corpus"));
        var index = new WikiIndex(fs, new MarkdigMarkdownParser(), new LinkResolver(fs), new Search.InMemoryFtsIndex());
        index.Rebuild();
        return index;
    }

    [Fact]
    public void Lists_all_notes()
    {
        var notes = BuildCorpusIndex().AllNotes();
        Assert.Contains("Home.md", notes);
        Assert.Contains("Projects.md", notes);
        Assert.Contains("sub/Nested.md", notes);
    }

    [Fact]
    public void Outbound_links_are_indexed()
    {
        var links = BuildCorpusIndex().LinksOf("Home.md");
        Assert.Contains(links, l => l.Target == "Projects");
        Assert.Contains(links, l => l.Target == "sub/Nested");
    }

    [Fact]
    public void Backlinks_are_the_inverse()
    {
        var back = BuildCorpusIndex().BacklinksOf("Home.md");
        Assert.Contains(back, b => b.FromNote == "Projects.md");
        Assert.Contains(back, b => b.FromNote == "sub/Nested.md");
    }

    [Fact]
    public void Tag_membership()
    {
        var index = BuildCorpusIndex();
        Assert.Contains("Home.md", index.NotesWithTag("index"));
        Assert.Contains("Projects.md", index.NotesWithTag("index"));
        Assert.Contains("Projects.md", index.NotesWithTag("work"));
        Assert.DoesNotContain("Home.md", index.NotesWithTag("work"));
    }
}
