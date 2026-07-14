// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Models;

namespace Wiki.Core.Search;

/// <summary>A pure inverted index: term -> (notePath -> term frequency). Tokenizes on non-letter/digit
/// runs, case-folds. Multi-term queries AND the terms; score sums each term's tf·idf (rarer terms count
/// more) plus a boost when a query term appears in the note's title (filename). Proves search/ranking/
/// incremental-update logic headlessly; the SQLite/FTS5 cache reuses this seam (Wiki.Storage).</summary>
public sealed class InMemoryFtsIndex : IFtsIndex
{
    // notePath -> (term -> frequency) for the body, plus the note's title tokens.
    private readonly Dictionary<string, Dictionary<string, int>> _docs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _titles = new(StringComparer.Ordinal);

    private const double TitleBoost = 10.0;
    private static readonly HashSet<string> Empty = new(StringComparer.Ordinal);

    public void Add(string notePath, string content)
    {
        _docs[notePath] = Tokenize(content);
        _titles[notePath] = new HashSet<string>(Tokenize(TitleOf(notePath)).Keys, StringComparer.Ordinal);
    }

    public void Update(string notePath, string content) => Add(notePath, content);

    public void Remove(string notePath)
    {
        _docs.Remove(notePath);
        _titles.Remove(notePath);
    }

    public IReadOnlyList<SearchHit> Search(string query, int limit = 50)
    {
        var terms = Tokenize(query).Keys.ToList();
        if (terms.Count == 0) return Array.Empty<SearchHit>();

        int totalDocs = _docs.Count;
        // Document frequency per query term (how many notes contain it).
        var df = terms.ToDictionary(t => t, t => _docs.Count(d => d.Value.ContainsKey(t)), StringComparer.Ordinal);

        var hits = new List<SearchHit>();
        foreach (var (path, freqs) in _docs)
        {
            var title = _titles.TryGetValue(path, out var ts) ? ts : Empty;
            // AND semantics: every query term must appear in the body OR the note's title.
            if (!terms.All(t => freqs.ContainsKey(t) || title.Contains(t))) continue;
            double score = 0;
            foreach (var t in terms)
            {
                double idf = Math.Log(1.0 + (double)totalDocs / Math.Max(1, df[t]));
                if (freqs.TryGetValue(t, out var tf)) score += tf * idf;   // body contribution (0 if title-only)
                if (title.Contains(t)) score += TitleBoost;                // title boost
            }
            hits.Add(new SearchHit(path, score));
        }
        return hits
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.NotePath, StringComparer.Ordinal)   // stable tie-break
            .Take(limit)
            .ToList();
    }

    private static string TitleOf(string notePath)
    {
        int slash = notePath.LastIndexOf('/');
        string name = slash >= 0 ? notePath[(slash + 1)..] : notePath;
        return name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name;
    }

    private static Dictionary<string, int> Tokenize(string text)
    {
        var freqs = new Dictionary<string, int>(StringComparer.Ordinal);
        int i = 0;
        while (i < text.Length)
        {
            if (!char.IsLetterOrDigit(text[i])) { i++; continue; }
            int start = i;
            while (i < text.Length && char.IsLetterOrDigit(text[i])) i++;
            string term = text[start..i].ToLowerInvariant();
            freqs[term] = freqs.TryGetValue(term, out var c) ? c + 1 : 1;
        }
        return freqs;
    }
}
