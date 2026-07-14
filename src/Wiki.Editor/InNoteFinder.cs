// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Editor;

/// <summary>Pure in-document text search for the Ctrl+F find bar. Returns every match range in document
/// order; <see cref="Next"/> picks the next/previous match relative to the caret, wrapping around. No
/// Avalonia — unit-tested headlessly. The surface highlights a chosen match by selecting its range.</summary>
public static class InNoteFinder
{
    public readonly record struct Match(int Start, int Length);

    public static IReadOnlyList<Match> Find(string text, string query, bool matchCase)
    {
        var result = new List<Match>();
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text)) return result;
        var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int i = 0;
        while (i <= text.Length - query.Length)
        {
            int hit = text.IndexOf(query, i, cmp);
            if (hit < 0) break;
            result.Add(new Match(hit, query.Length));
            i = hit + query.Length;   // non-overlapping, mirrors editor find
        }
        return result;
    }

    /// <summary>The index (into <paramref name="matches"/>) of the next match after <paramref name="fromOffset"/>
    /// when <paramref name="forward"/>, else the previous match before it; wraps at the ends. Returns -1 for an
    /// empty list.</summary>
    public static int Next(IReadOnlyList<Match> matches, int fromOffset, bool forward)
    {
        if (matches.Count == 0) return -1;
        if (forward)
        {
            for (int i = 0; i < matches.Count; i++)
                if (matches[i].Start > fromOffset) return i;
            return 0;   // wrap
        }
        for (int i = matches.Count - 1; i >= 0; i--)
            if (matches[i].Start < fromOffset) return i;
        return matches.Count - 1;   // wrap
    }

    /// <summary>Replaces every (non-overlapping) match of <paramref name="query"/> with
    /// <paramref name="replacement"/>; <paramref name="count"/> reports how many. Pure — the find bar
    /// applies the result as one document replace (a single undo unit).</summary>
    public static string ReplaceAll(string text, string query, string replacement, bool matchCase, out int count)
    {
        var matches = Find(text, query, matchCase);
        count = matches.Count;
        if (count == 0) return text;
        var sb = new System.Text.StringBuilder(text.Length + count * (replacement.Length - query.Length));
        int last = 0;
        foreach (var m in matches)
        {
            sb.Append(text, last, m.Start - last).Append(replacement);
            last = m.Start + m.Length;
        }
        sb.Append(text, last, text.Length - last);
        return sb.ToString();
    }
}
