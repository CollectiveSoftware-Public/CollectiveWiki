// SPDX-License-Identifier: GPL-3.0-or-later
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Wiki.Core.Parsing;

namespace Wiki.Core.Indexing;

/// <summary>A plain-text occurrence of a note's title/alias in another note that is NOT already a
/// <c>[[wikilink]]</c> — the raw material for a one-click "link". End-exclusive match span.</summary>
public sealed record UnlinkedMention(
    string SourceNotePath, int Line, int MatchStart, int MatchLength, string Snippet);

/// <summary>Pure: finds unlinked mentions of a target note's names across other notes, excluding code
/// (blocks + inline), existing wikilinks, and the front-matter block. Whole-word, case-insensitive.</summary>
public static class UnlinkedMentions
{
    private static readonly MarkdigMarkdownParser Parser = new();

    public static IReadOnlyList<UnlinkedMention> Find(
        string targetTitle, IEnumerable<string> aliases,
        IReadOnlyList<(string Path, string Text)> otherNotes)
    {
        var names = new List<string> { targetTitle };
        names.AddRange(aliases);
        names = names.Where(n => !string.IsNullOrWhiteSpace(n) && n.Trim().Length >= 2)
                     .Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var results = new List<UnlinkedMention>();
        if (names.Count == 0) return results;

        foreach (var (path, text) in otherNotes)
        {
            var excluded = ExcludedSpans(text);
            foreach (var name in names)
            {
                int from = 0, idx;
                while ((idx = text.IndexOf(name, from, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    from = idx + 1;
                    if (!IsWholeWord(text, idx, name.Length)) continue;
                    if (InAny(idx, excluded)) continue;
                    results.Add(new UnlinkedMention(path, LineOf(text, idx), idx, name.Length,
                        Snippet(text, idx)));
                }
            }
        }
        return results;
    }

    // Code blocks/inline + existing [[links]] + the YAML front-matter block, as [start,end) ranges.
    private static List<(int Start, int End)> ExcludedSpans(string text)
    {
        var spans = new List<(int, int)>();
        var ast = Parser.Parse(text);
        foreach (var node in ast.Document.Descendants())
            switch (node)
            {
                case CodeBlock cb: spans.Add((cb.Span.Start, cb.Span.End + 1)); break;   // Markdig End inclusive
                case CodeInline ci: spans.Add((ci.Span.Start, ci.Span.End + 1)); break;
            }
        foreach (var l in ast.Links) spans.Add((l.SourceStart, l.SourceEnd));
        if (ast.Document.Descendants<YamlFrontMatterBlock>().FirstOrDefault() is { } fm)
            spans.Add((fm.Span.Start, fm.Span.End + 1));
        return spans;
    }

    private static bool InAny(int offset, List<(int Start, int End)> spans)
    {
        foreach (var (s, e) in spans) if (offset >= s && offset < e) return true;
        return false;
    }

    private static bool IsWholeWord(string text, int start, int len)
    {
        bool leftOk = start == 0 || !IsWordChar(text[start - 1]);
        int end = start + len;
        bool rightOk = end >= text.Length || !IsWordChar(text[end]);
        return leftOk && rightOk;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static int LineOf(string text, int offset)
    {
        int line = 0;
        for (int i = 0; i < offset && i < text.Length; i++) if (text[i] == '\n') line++;
        return line;
    }

    private static string Snippet(string text, int start)
    {
        int ls = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        int le = text.IndexOf('\n', start); if (le < 0) le = text.Length;
        string line = text[ls..le].Trim();
        return line.Length <= 120 ? line : line[..120] + "…";
    }
}

/// <summary>Pure: rewrites a source note's text to link one or more unlinked mentions to the target note.</summary>
public static class MentionLinker
{
    public static string LinkOne(string sourceText, UnlinkedMention m, string targetTitle)
    {
        string surface = sourceText.Substring(m.MatchStart, m.MatchLength);
        string link = string.Equals(surface, targetTitle, StringComparison.Ordinal)
            ? $"[[{targetTitle}]]" : $"[[{targetTitle}|{surface}]]";
        return sourceText[..m.MatchStart] + link + sourceText[(m.MatchStart + m.MatchLength)..];
    }

    public static string LinkAll(string sourceText, IEnumerable<UnlinkedMention> mentions, string targetTitle)
    {
        foreach (var m in mentions.OrderByDescending(x => x.MatchStart))   // back-to-front keeps offsets valid
            sourceText = LinkOne(sourceText, m, targetTitle);
        return sourceText;
    }
}
