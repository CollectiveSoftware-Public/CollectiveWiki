// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Search;

namespace Wiki.Core.Tests;

public class FtsIndexTests
{
    private static InMemoryFtsIndex Seed()
    {
        var fts = new InMemoryFtsIndex();
        fts.Add("A.md", "the quick brown fox");
        fts.Add("B.md", "the lazy dog sleeps; the dog barks");
        fts.Add("C.md", "quick quick quick notes");
        return fts;
    }

    [Fact]
    public void Finds_notes_containing_a_term()
    {
        var hits = Seed().Search("quick");
        Assert.Contains(hits, h => h.NotePath == "A.md");
        Assert.Contains(hits, h => h.NotePath == "C.md");
        Assert.DoesNotContain(hits, h => h.NotePath == "B.md");
    }

    [Fact]
    public void Ranks_higher_term_frequency_first()
    {
        var hits = Seed().Search("quick");
        Assert.Equal("C.md", hits[0].NotePath);   // "quick" x3 outranks x1
    }

    [Fact]
    public void Multi_term_query_requires_all_terms()
    {
        var hits = Seed().Search("dog barks");
        Assert.Single(hits);
        Assert.Equal("B.md", hits[0].NotePath);
    }

    [Fact]
    public void Update_and_remove_change_results()
    {
        var fts = Seed();
        fts.Remove("C.md");
        Assert.DoesNotContain(fts.Search("quick"), h => h.NotePath == "C.md");
        fts.Update("A.md", "no longer mentions the animal");
        Assert.DoesNotContain(fts.Search("quick"), h => h.NotePath == "A.md");
    }

    [Fact]
    public void Search_is_case_insensitive_and_ignores_punctuation()
    {
        var fts = new InMemoryFtsIndex();
        fts.Add("X.md", "Hello, WORLD!");
        Assert.Single(fts.Search("world"));
    }
}
