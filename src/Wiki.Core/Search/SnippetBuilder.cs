// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Search;

/// <summary>Pure: extracts up to <c>maxSnippets</c> matching-line snippets from a note for the search pane.
/// Case-insensitive, first match per line, document order. The snippet <c>Text</c> is the trimmed line, and
/// <c>MatchStart</c> is the query's offset within that trimmed text. UI-free + unit-tested.</summary>
public static class SnippetBuilder
{
    public static IReadOnlyList<SearchSnippet> Build(string noteText, string query, int maxSnippets = 3)
    {
        var result = new List<SearchSnippet>();
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrEmpty(noteText)) return result;
        string q = query.Trim();
        var lines = noteText.Split('\n');
        for (int i = 0; i < lines.Length && result.Count < maxSnippets; i++)
        {
            string line = lines[i].Replace('\r', ' ');
            int idx = line.IndexOf(q, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            int leadingTrim = line.Length - line.TrimStart().Length;
            int matchStart = Math.Max(0, idx - leadingTrim);
            result.Add(new SearchSnippet(i, line.Trim(), matchStart, q.Length));
        }
        return result;
    }
}
