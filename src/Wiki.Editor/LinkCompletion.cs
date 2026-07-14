// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Search;

namespace Wiki.Editor;

/// <summary>Pure logic for the editor's <c>[[</c> wikilink autocomplete: detect the link context at
/// the caret, rank note-title candidates (via the shared <see cref="QuickSwitcher.Rank"/> so it feels
/// like Ctrl+O), and build the commit edit. The surface draws the popup + routes keys; everything
/// here is Avalonia-free and unit-tested.</summary>
public static class LinkCompletion
{
    /// <summary>A live <c>[[</c> context: <see cref="Query"/> is the text between <c>[[</c> and the
    /// caret; <see cref="CloseStart"/> is the absolute offset of a same-line closing <c>]]</c> when the
    /// caret sits inside an existing link, else −1.</summary>
    public readonly record struct Context(int QueryStart, string Query, int CloseStart);

    public static Context? Detect(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        int ls = caret <= 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        string before = text[ls..caret];
        int open = before.LastIndexOf("[[", StringComparison.Ordinal);
        if (open < 0) return null;
        string query = before[(open + 2)..];
        if (query.Contains('[') || query.Contains(']')) return null;   // the [[ before the caret is finished business
        int qs = ls + open + 2;

        int nl = caret >= text.Length ? -1 : text.IndexOf('\n', caret);
        int le = nl < 0 ? text.Length : nl;
        int close = caret >= text.Length ? -1 : text.IndexOf("]]", caret, StringComparison.Ordinal);
        if (close >= 0 && close < le)
        {
            string rest = text[caret..close];
            if (!rest.Contains('[') && !rest.Contains(']')) return new(qs, query, close);
        }
        return new(qs, query, -1);
    }

    /// <summary>Note titles (file name without <c>.md</c>) matching <paramref name="query"/>, ranked
    /// exact → prefix → substring then alphabetical; an empty query lists the first titles alphabetically.</summary>
    public static IReadOnlyList<string> Candidates(IReadOnlyList<string> notePaths, string query, int limit = 8)
    {
        var titles = notePaths
            .Select(Title)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
            return titles.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).Take(limit).ToList();
        return titles
            .Select(t => (t, rank: QuickSwitcher.Rank(t, query.Trim())))
            .Where(x => x.rank >= 0)
            .OrderBy(x => x.rank).ThenBy(x => x.t, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.t).Take(limit).ToList();
    }

    private static string Title(string path)
    {
        int sl = path.LastIndexOf('/');
        string f = sl < 0 ? path : path[(sl + 1)..];
        return f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? f[..^3] : f;
    }

    /// <summary>The edit committing <paramref name="title"/>: inside an existing link the whole old
    /// target is replaced (keeping its <c>]]</c>); in an open link the typed query is replaced and the
    /// link closed. The caret lands just past the <c>]]</c>.</summary>
    public static MarkdownEditing.FormatResult Commit(string text, Context ctx, int caret, string title)
    {
        if (ctx.CloseStart >= 0)
        {
            string t = text[..ctx.QueryStart] + title + text[ctx.CloseStart..];
            int c = ctx.QueryStart + title.Length + 2;
            return new(t, c, c);
        }
        string ins = title + "]]";
        string nt = text[..ctx.QueryStart] + ins + text[Math.Clamp(caret, 0, text.Length)..];
        int nc = ctx.QueryStart + ins.Length;
        return new(nt, nc, nc);
    }
}
