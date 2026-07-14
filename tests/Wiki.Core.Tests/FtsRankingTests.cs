// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Search;

namespace Wiki.Core.Tests;

public class FtsRankingTests
{
    [Fact]
    public void A_title_match_outranks_a_body_only_match()
    {
        var fts = new InMemoryFtsIndex();
        fts.Add("Widgets.md", "a short note");                 // term in the TITLE
        fts.Add("Other.md", "widgets widgets widgets here");   // term thrice in the BODY

        var hits = fts.Search("widgets");
        Assert.Equal("Widgets.md", hits[0].NotePath);          // title boost wins
    }

    [Fact]
    public void Rarer_terms_contribute_more_than_common_ones()
    {
        var fts = new InMemoryFtsIndex();
        fts.Add("A.md", "common common rare");     // has the rare term once
        fts.Add("B.md", "common common common");   // only the common term
        fts.Add("C.md", "common rare");

        // "common" appears everywhere (low idf); "rare" only in A and C (higher idf).
        var hits = fts.Search("common rare");
        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.NotePath == "A.md");
        Assert.Contains(hits, h => h.NotePath == "C.md");
        Assert.DoesNotContain(hits, h => h.NotePath == "B.md");
    }
}
