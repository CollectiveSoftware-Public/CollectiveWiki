// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Models;

namespace Wiki.Core.Search;

public enum SwitcherHitKind { Title, Content }

/// <summary>One quick-switcher result. <see cref="Folder"/> is the note's directory (<c>""</c> at the
/// vault root); <see cref="Snippet"/> is set only for <see cref="SwitcherHitKind.Content"/> hits (the
/// line of the note that matched).</summary>
public sealed record SwitcherHit(string NotePath, string Title, string Folder, SwitcherHitKind Kind, string? Snippet);

/// <summary>Pure ranking for the Ctrl+O quick switcher. Title matches come first — exact, then prefix,
/// then substring (case-insensitive), ties broken by a shallower path then ordinal path — followed by
/// full-text content matches (from the vault index) that the title tier didn't already list. UI-free and
/// unit-tested; the head renders the returned <see cref="SwitcherHit"/>s.</summary>
public static class QuickSwitcher
{
    /// <param name="notePaths">All '/'-relative note paths in the vault.</param>
    /// <param name="query">The user's typed query.</param>
    /// <param name="contentSearch">The vault's full-text search (tf·idf+title), score-ordered.</param>
    /// <param name="readNote">Reads a note's raw text (only called for content hits, to build a snippet).</param>
    /// <param name="limit">Maximum results, title tier first.</param>
    public static IReadOnlyList<SwitcherHit> Query(
        IReadOnlyList<string> notePaths, string query,
        Func<string, IReadOnlyList<SearchHit>> contentSearch, Func<string, string> readNote,
        int limit = 12)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SwitcherHit>();
        string q = query.Trim();

        // ---- title tier: exact (0) → prefix (1) → substring (2); tie → shallower path, then ordinal ----
        var titled = new List<(int rank, int depth, string path)>();
        foreach (var path in notePaths)
        {
            int rank = Rank(TitleOf(path), q);
            if (rank >= 0) titled.Add((rank, Depth(path), path));
        }
        titled.Sort((a, b) =>
        {
            int c = a.rank.CompareTo(b.rank);
            if (c != 0) return c;
            c = a.depth.CompareTo(b.depth);
            return c != 0 ? c : string.CompareOrdinal(a.path, b.path);
        });

        var results = new List<SwitcherHit>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, _, path) in titled)
        {
            if (results.Count >= limit) break;
            results.Add(new SwitcherHit(path, TitleOf(path), FolderOf(path), SwitcherHitKind.Title, null));
            taken.Add(path);
        }

        // ---- content tier: index hits not already surfaced by a title match, in index-score order ----
        if (results.Count < limit)
        {
            foreach (var hit in contentSearch(q))
            {
                if (results.Count >= limit) break;
                if (!taken.Add(hit.NotePath)) continue;   // already a title hit (or a duplicate)
                results.Add(new SwitcherHit(hit.NotePath, TitleOf(hit.NotePath), FolderOf(hit.NotePath),
                    SwitcherHitKind.Content, Snippet(readNote(hit.NotePath), q)));
            }
        }

        return results;
    }

    /// <summary>Shared fuzzy rank of <paramref name="text"/> against query <paramref name="q"/>:
    /// 0 = exact (case-insensitive), 1 = prefix, 2 = substring, -1 = no match. Reused by the command
    /// palette (<c>CommandRegistry</c>) so it ranks like the quick switcher.</summary>
    public static int Rank(string text, string q)
    {
        if (string.Equals(text, q, StringComparison.OrdinalIgnoreCase)) return 0;
        if (text.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 1;
        if (text.Contains(q, StringComparison.OrdinalIgnoreCase)) return 2;
        return -1;
    }

    private static string TitleOf(string path)
    {
        int slash = path.LastIndexOf('/');
        string name = slash >= 0 ? path[(slash + 1)..] : path;
        return name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name;
    }

    private static string FolderOf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }

    private static int Depth(string path)
    {
        int n = 0;
        foreach (char c in path) if (c == '/') n++;
        return n;
    }

    /// <summary>The first line of <paramref name="text"/> containing <paramref name="q"/> (case-insensitive),
    /// trimmed to a ≤ 80-char window centred on the match (ellipsis-marked when clipped); null when no line
    /// matches.</summary>
    private static string? Snippet(string text, string q)
    {
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Replace('\r', ' ').Trim();
            int idx = line.IndexOf(q, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            if (line.Length <= 80) return line;

            int budget = Math.Max(q.Length, 78);   // leave room for up to two 1-char ellipses
            int half = (budget - q.Length) / 2;
            int start = Math.Clamp(idx - half, 0, Math.Max(0, line.Length - budget));
            int len = Math.Min(budget, line.Length - start);
            string slice = line.Substring(start, len);
            if (start > 0) slice = "…" + slice;
            if (start + len < line.Length) slice += "…";
            return slice;
        }
        return null;
    }
}
