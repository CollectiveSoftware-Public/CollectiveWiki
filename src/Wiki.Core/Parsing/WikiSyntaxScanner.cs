// SPDX-License-Identifier: GPL-3.0-or-later
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Wiki.Core.Models;

namespace Wiki.Core.Parsing;

/// <summary>Extracts wiki syntax (<c>[[links]]</c>, <c>![[embeds]]</c>, <c>#tags</c>) from the raw note
/// text, using the parsed Markdig AST only to identify code regions to skip. We scan raw text (not the
/// inline AST) because Markdig consumes <c>[</c>/<c>]</c> as link-delimiter syntax and splits
/// <c>[[Note]]</c> across several inline nodes; raw scanning keeps each link contiguous AND yields exact
/// absolute offsets. Code spans (fenced/indented blocks + inline code) are excluded via their source
/// spans so links/tags inside code are ignored. Heading <c>#</c> is excluded by the word-boundary rule.</summary>
public sealed class WikiSyntaxScanner
{
    public (IReadOnlyList<WikiLink>, IReadOnlyList<TagRef>) Scan(string text, MarkdownDocument doc)
    {
        var codeSpans = CollectCodeSpans(doc);
        var links = new List<WikiLink>();
        var tags = new List<TagRef>();
        ScanText(text, codeSpans, links, tags);
        return (links, tags);
    }

    // Fenced + indented code blocks (CodeBlock covers both) and inline code, as absolute [Start, End]
    // ranges (End inclusive, per Markdig SourceSpan).
    private static List<(int Start, int End)> CollectCodeSpans(MarkdownDocument doc)
    {
        var spans = new List<(int, int)>();
        foreach (var node in doc.Descendants())
        {
            switch (node)
            {
                case CodeBlock cb: spans.Add((cb.Span.Start, cb.Span.End)); break;
                case CodeInline ci: spans.Add((ci.Span.Start, ci.Span.End)); break;
            }
        }
        return spans;
    }

    private static bool InCode(int offset, List<(int Start, int End)> codeSpans)
    {
        foreach (var (s, e) in codeSpans)
            if (offset >= s && offset <= e) return true;
        return false;
    }

    private static void ScanText(string text, List<(int Start, int End)> codeSpans,
        List<WikiLink> links, List<TagRef> tags)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '[' && i + 1 < text.Length && text[i + 1] == '[' && !InCode(i, codeSpans))
            {
                bool isEmbed = i > 0 && text[i - 1] == '!';
                int close = text.IndexOf("]]", i + 2, StringComparison.Ordinal);
                if (close > i + 1)
                {
                    string inner = text.Substring(i + 2, close - (i + 2));
                    int start = isEmbed ? i - 1 : i;
                    int end = close + 2;     // exclusive
                    links.Add(ParseWikiLink(inner, isEmbed, start, end));
                    i = close + 2;
                    continue;
                }
            }
            if (text[i] == '#' && IsTagStart(text, i) && !InCode(i, codeSpans))
            {
                int j = i + 1;
                while (j < text.Length && IsTagChar(text[j])) j++;
                if (j > i + 1)
                {
                    tags.Add(new TagRef(text.Substring(i + 1, j - (i + 1)), i, j));
                    i = j;
                    continue;
                }
            }
            i++;
        }
    }

    private static WikiLink ParseWikiLink(string inner, bool isEmbed, int start, int end)
    {
        string target = inner;
        string? alias = null, heading = null;
        int pipe = inner.IndexOf('|');
        if (pipe >= 0) { alias = inner[(pipe + 1)..].Trim(); target = inner[..pipe]; }
        int hash = target.IndexOf('#');
        if (hash >= 0) { heading = target[(hash + 1)..].Trim(); target = target[..hash]; }
        return new WikiLink(target.Trim(), heading, alias, isEmbed, start, end);
    }

    // A tag '#' must sit at a word boundary (start of text or after a non-alphanumeric char) so that
    // 'a#b' is not a tag. A heading '# ' is rejected because the char after '#' (a space) is not a tag char.
    private static bool IsTagStart(string text, int i) => i == 0 || !char.IsLetterOrDigit(text[i - 1]);
    private static bool IsTagChar(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '/';

    /// <summary>Reads simple <c>key: value</c> lines from a leading YAML front-matter block. Intentionally
    /// minimal (no nested YAML) — enough for title/aliases/tags fields; keeps Core dependency-light.</summary>
    public IReadOnlyDictionary<string, string> ReadFrontmatter(MarkdownDocument doc)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fm = doc.Descendants<YamlFrontMatterBlock>().FirstOrDefault();
        if (fm?.Lines.Lines is { } lines)
        {
            foreach (var line in lines)
            {
                string s = line.Slice.ToString();
                if (string.IsNullOrWhiteSpace(s)) continue;
                int colon = s.IndexOf(':');
                if (colon > 0)
                    result[s[..colon].Trim()] = s[(colon + 1)..].Trim();
            }
        }
        return result;
    }
}
