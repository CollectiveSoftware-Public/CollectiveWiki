// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Search;
using Wiki.Storage;

namespace Wiki.Storage.Tests;

public class SqliteFtsIndexTests
{
    // A private in-memory DB lives as long as its single open connection — perfect for a headless test.
    private static SqliteFtsIndex NewIndex() => new(":memory:");

    [Fact]
    public void Finds_notes_containing_a_term()
    {
        using var fts = NewIndex();
        fts.Add("A.md", "the quick brown fox");
        fts.Add("B.md", "the lazy dog sleeps");
        fts.Add("C.md", "quick quick quick notes");

        var hits = fts.Search("quick");
        Assert.Contains(hits, h => h.NotePath == "A.md");
        Assert.Contains(hits, h => h.NotePath == "C.md");
        Assert.DoesNotContain(hits, h => h.NotePath == "B.md");
    }

    [Fact]
    public void Ranks_higher_term_frequency_first()
    {
        using var fts = NewIndex();
        fts.Add("A.md", "the quick brown fox");
        fts.Add("C.md", "quick quick quick notes");
        Assert.Equal("C.md", fts.Search("quick")[0].NotePath);
    }

    [Fact]
    public void Multi_term_query_requires_all_terms()
    {
        using var fts = NewIndex();
        fts.Add("A.md", "dog and cat");
        fts.Add("B.md", "the lazy dog barks loudly");
        var hits = fts.Search("dog barks");
        Assert.Single(hits);
        Assert.Equal("B.md", hits[0].NotePath);
    }

    [Fact]
    public void Update_and_remove_change_results()
    {
        using var fts = NewIndex();
        fts.Add("A.md", "mentions zebra");
        fts.Add("B.md", "mentions zebra too");
        fts.Remove("B.md");
        Assert.Single(fts.Search("zebra"));
        fts.Update("A.md", "no longer about the animal");
        Assert.Empty(fts.Search("zebra"));
    }

    [Fact]
    public void Persists_across_reopen()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wiki-fts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db = Path.Combine(dir, "index.db");
        try
        {
            using (var fts = new SqliteFtsIndex(db)) fts.Add("A.md", "durable content here");
            using (var reopened = new SqliteFtsIndex(db))
                Assert.Contains(reopened.Search("durable"), h => h.NotePath == "A.md");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }
}
