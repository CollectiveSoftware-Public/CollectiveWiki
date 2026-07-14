// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Search;

/// <summary>A parsed search box query: plain AND <see cref="Terms"/> (fed to FTS) plus
/// <c>tag:</c> / <c>path:</c> / <c>"quoted phrase"</c> filters. Pure + unit-tested; an unknown
/// <c>word:</c> prefix falls through to a plain term.</summary>
public sealed record SearchQuery(
    IReadOnlyList<string> Terms,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Paths,
    IReadOnlyList<string> Phrases)
{
    public bool HasFilters => Tags.Count > 0 || Paths.Count > 0 || Phrases.Count > 0;
    public string TermQuery => string.Join(' ', Terms);

    public static SearchQuery Parse(string? raw)
    {
        var terms = new List<string>(); var tags = new List<string>();
        var paths = new List<string>(); var phrases = new List<string>();
        foreach (var tok in Tokenize(raw ?? ""))
        {
            if (tok.Length >= 2 && tok[0] == '"' && tok[^1] == '"') phrases.Add(tok[1..^1]);
            else if (tok.StartsWith("tag:", StringComparison.OrdinalIgnoreCase) && tok.Length > 4)
                tags.Add(tok[4..].TrimStart('#'));
            else if (tok.StartsWith("path:", StringComparison.OrdinalIgnoreCase) && tok.Length > 5)
                paths.Add(tok[5..]);
            else terms.Add(tok);
        }
        return new(terms, tags, paths, phrases);
    }

    // Whitespace split, but a "quoted phrase" (with spaces) is one token (quotes kept; Parse strips them).
    private static IEnumerable<string> Tokenize(string s)
    {
        int i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length) break;
            int start = i;
            if (s[i] == '"') { i++; while (i < s.Length && s[i] != '"') i++; if (i < s.Length) i++; }
            else while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
            yield return s[start..i];
        }
    }
}
