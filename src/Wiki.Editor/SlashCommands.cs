// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Search;

namespace Wiki.Editor;

/// <summary>One entry in the editor's <c>/</c> command menu: a stable <see cref="Id"/> the surface maps to
/// an editor action, a display <see cref="Label"/>, and extra <see cref="Keywords"/> for ranking.</summary>
public sealed record SlashCommand(string Id, string Label, string Keywords);

/// <summary>The fixed set of slash commands + ranking (via the shared <see cref="QuickSwitcher.Rank"/>).</summary>
public static class SlashCommands
{
    public static readonly IReadOnlyList<SlashCommand> All = new[]
    {
        new SlashCommand("h1", "Heading 1", "title header h1"),
        new SlashCommand("h2", "Heading 2", "header h2"),
        new SlashCommand("h3", "Heading 3", "header h3"),
        new SlashCommand("bullet", "Bullet list", "unordered ul dash"),
        new SlashCommand("numbered", "Numbered list", "ordered ol"),
        new SlashCommand("task", "Task list", "todo checkbox check"),
        new SlashCommand("quote", "Quote", "blockquote"),
        new SlashCommand("code", "Code block", "fence pre"),
        new SlashCommand("callout", "Callout", "admonition note warning"),
        new SlashCommand("table", "Table", "grid"),
        new SlashCommand("date", "Insert date", "today"),
        new SlashCommand("template", "Insert template", "snippet"),
    };

    public static IReadOnlyList<SlashCommand> Candidates(string query, int limit = 8)
    {
        if (string.IsNullOrWhiteSpace(query)) return All.Take(limit).ToList();
        string q = query.Trim();
        return All
            .Select(c => (c, rank: BestRank(c.Label + " " + c.Keywords, q)))
            .Where(x => x.rank >= 0)
            .OrderBy(x => x.rank).ThenBy(x => x.c.Label, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.c).Take(limit).ToList();
    }

    // The best (lowest) matching rank across the label's + keywords' words, or -1 if none match.
    private static int BestRank(string text, string q)
    {
        int best = -1;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int r = QuickSwitcher.Rank(word, q);
            if (r >= 0 && (best < 0 || r < best)) best = r;
        }
        return best;
    }
}
