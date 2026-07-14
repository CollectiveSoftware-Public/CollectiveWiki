// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Search;

namespace Wiki.Editor;

/// <summary>Pure logic for the editor's <c>#</c> tag autocomplete — the tag counterpart of
/// <see cref="LinkCompletion"/>. Detects a tag being typed at the caret (a <c>#</c> at a word boundary
/// followed by tag chars: letters/digits/<c>-</c>/<c>_</c>/<c>/</c>), ranks known tags via the shared
/// <see cref="QuickSwitcher.Rank"/>, and builds the commit edit. Avalonia-free; unit-tested.</summary>
public static class TagCompletion
{
    public readonly record struct Context(int QueryStart, string Query);

    private static bool IsTagChar(char c) => char.IsLetterOrDigit(c) || c is '-' or '_' or '/';

    public static Context? Detect(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        int i = caret;
        while (i > 0 && IsTagChar(text[i - 1])) i--;             // walk back over the tag run
        if (i == 0 || text[i - 1] != '#') return null;            // must be preceded by '#'
        int hash = i - 1;
        if (hash > 0 && char.IsLetterOrDigit(text[hash - 1])) return null;   // '#' at a word boundary only
        string query = text[i..caret];
        if (query.Length == 0 && caret < text.Length && IsTagChar(text[caret])) return null;   // caret mid-tag
        return new Context(i, query);
    }

    public static IReadOnlyList<string> Candidates(IReadOnlyList<string> allTags, string query, int limit = 8)
    {
        if (string.IsNullOrWhiteSpace(query))
            return allTags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).Take(limit).ToList();
        return allTags
            .Select(t => (t, rank: QuickSwitcher.Rank(t, query.Trim())))
            .Where(x => x.rank >= 0)
            .OrderBy(x => x.rank).ThenBy(x => x.t, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.t).Take(limit).ToList();
    }

    public static MarkdownEditing.FormatResult Commit(string text, Context ctx, int caret, string tag)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        string nt = text[..ctx.QueryStart] + tag + text[caret..];
        int nc = ctx.QueryStart + tag.Length;
        return new(nt, nc, nc);
    }
}
