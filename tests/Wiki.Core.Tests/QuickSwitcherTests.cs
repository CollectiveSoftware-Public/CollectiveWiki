// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Linq;
using Wiki.Core.Models;
using Wiki.Core.Search;
using Xunit;

namespace Wiki.Core.Tests;

public class QuickSwitcherTests
{
    private static IReadOnlyList<SearchHit> NoContent(string _) => Array.Empty<SearchHit>();
    private static string NoRead(string _) => "";

    [Fact]
    public void Title_tier_ranks_exact_then_prefix_then_substring()
    {
        var notes = new[] { "Vise Grip.md", "Bench Vise.md", "Vise.md" };

        var r = QuickSwitcher.Query(notes, "vise", NoContent, NoRead);

        Assert.Equal(new[] { "Vise.md", "Vise Grip.md", "Bench Vise.md" }, r.Select(h => h.NotePath));
        Assert.All(r, h => Assert.Equal(SwitcherHitKind.Title, h.Kind));
    }

    [Fact]
    public void A_shallower_path_wins_a_rank_tie()
    {
        var notes = new[] { "A/B/Note.md", "Note.md", "X/Note.md" };   // all exact-title "Note"

        var r = QuickSwitcher.Query(notes, "note", NoContent, NoRead);

        Assert.Equal(new[] { "Note.md", "X/Note.md", "A/B/Note.md" }, r.Select(h => h.NotePath));
    }

    [Fact]
    public void Content_hits_are_appended_after_title_hits_with_a_snippet()
    {
        var notes = new[] { "Bench Vise.md", "Milling Machine.md" };
        IReadOnlyList<SearchHit> content(string _) => new[] { new SearchHit("Milling Machine.md", 1.0) };
        string read(string p) => p == "Milling Machine.md" ? "Clamp the work in the vise before cutting." : "";

        var r = QuickSwitcher.Query(notes, "vise", content, read);

        Assert.Equal("Bench Vise.md", r[0].NotePath);
        Assert.Equal(SwitcherHitKind.Title, r[0].Kind);
        Assert.Null(r[0].Snippet);

        Assert.Equal("Milling Machine.md", r[1].NotePath);
        Assert.Equal(SwitcherHitKind.Content, r[1].Kind);
        Assert.NotNull(r[1].Snippet);
        Assert.Contains("vise", r[1].Snippet!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_title_match_is_not_duplicated_by_the_content_tier()
    {
        var notes = new[] { "Bench Vise.md" };
        IReadOnlyList<SearchHit> content(string _) => new[] { new SearchHit("Bench Vise.md", 1.0) };

        var r = QuickSwitcher.Query(notes, "vise", content, _ => "vise");

        var hit = Assert.Single(r);
        Assert.Equal(SwitcherHitKind.Title, hit.Kind);   // surfaced once, as a title hit
    }

    [Fact]
    public void Blank_query_returns_empty()
    {
        Assert.Empty(QuickSwitcher.Query(new[] { "A.md" }, "   ", NoContent, NoRead));
    }

    [Fact]
    public void Limit_is_respected_across_both_tiers()
    {
        var notes = new[] { "Vise 1.md", "Vise 2.md", "Vise 3.md" };   // three title matches
        IReadOnlyList<SearchHit> content(string _) => new[] { new SearchHit("Other.md", 1.0) };

        var r = QuickSwitcher.Query(notes, "vise", content, _ => "vise here", limit: 2);

        Assert.Equal(2, r.Count);                          // capped; title tier fills first
        Assert.All(r, h => Assert.Equal(SwitcherHitKind.Title, h.Kind));
    }

    [Fact]
    public void Folder_is_the_directory_part_or_empty_at_the_root()
    {
        var r = QuickSwitcher.Query(new[] { "Machines/Bench Vise.md", "B.md" }, "b", NoContent, NoRead);

        Assert.Equal("Machines", r.First(h => h.NotePath == "Machines/Bench Vise.md").Folder);
        Assert.Equal("", r.First(h => h.NotePath == "B.md").Folder);
    }
}
