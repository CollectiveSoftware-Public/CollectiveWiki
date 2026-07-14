// SPDX-License-Identifier: GPL-3.0-or-later
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Footnotes;
using Markdig.Syntax;

namespace Wiki.Core.Parsing;

/// <summary>The default <see cref="IMarkdownParser"/>: Markdig for the block/inline AST, plus a pure
/// scan of the AST's literal runs for wiki syntax (added in <see cref="WikiSyntaxScanner"/>).</summary>
public sealed class MarkdigMarkdownParser : IMarkdownParser
{
    // Pipeline is immutable + thread-safe once built; build it once. YAML front matter is recognized
    // as a leading block so it doesn't pollute the body AST. PreciseSourceLocation tracks absolute
    // source spans on INLINE nodes too (e.g. CodeInline) — needed so the scanner can exclude wiki
    // syntax that sits inside inline code. EmphasisExtras is scoped to just Marked (`==highlight==`) and
    // Strikethrough (`~~strike~~`) — the two the toolbar exposes — so `~sub~`/`^sup^`/`++ins++` stay
    // literal (no surprise emphasis on a `~` or `^` typed in prose).
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder { PreciseSourceLocation = true }
            .UseYamlFrontMatter()
            .UseEmphasisExtras(EmphasisExtraOptions.Marked | EmphasisExtraOptions.Strikethrough)
            .UseFootnotes()
            .Build();

    private readonly WikiSyntaxScanner _scanner = new();

    public WikiAst Parse(string text)
    {
        text ??= "";
        var doc = Markdig.Markdown.Parse(text, Pipeline);
        var (links, tags) = _scanner.Scan(text, doc);
        var frontmatter = _scanner.ReadFrontmatter(doc);

        // Markdig image nodes (`![alt](path)`) are not wiki syntax, so the scanner misses them. Surface each
        // local image as an embed WikiLink so the live-preview surface renders it like an `![[embed]]`.
        var withImages = AppendMarkdownImages(text, doc, links);
        // Footnote references (`[^1]`) surface as links with target "^N" so the surface renders them as a
        // clickable number and the head can scroll to the `[^1]:` definition (same trick as images above).
        var withFootnotes = AppendFootnoteRefs(text, doc, withImages);
        return new WikiAst(doc, withFootnotes, tags, frontmatter);
    }

    private static IReadOnlyList<Wiki.Core.Models.WikiLink> AppendFootnoteRefs(
        string text, MarkdownDocument doc, IReadOnlyList<Wiki.Core.Models.WikiLink> links)
    {
        List<Wiki.Core.Models.WikiLink>? extra = null;
        foreach (var node in doc.Descendants())
        {
            if (node is not FootnoteLink { IsBackLink: false } fl) continue;
            int start = fl.Span.Start, end = fl.Span.End + 1;            // Markdig End is inclusive
            if (start < 0 || end > text.Length || start >= end) continue;
            string span = text[start..end];                             // "[^label]"
            if (!span.StartsWith("[^", StringComparison.Ordinal) || !span.EndsWith("]", StringComparison.Ordinal))
                continue;
            // Target carries the definition label ("^note" → the `[^note]:` line) for click-to-scroll;
            // the alias is the display order number (footnotes read as ¹²³).
            string label = span[2..^1];
            (extra ??= new()).Add(new Wiki.Core.Models.WikiLink(
                "^" + label, null, fl.Footnote.Order.ToString(), IsEmbed: false, start, end));
        }
        if (extra is null) return links;
        var merged = new List<Wiki.Core.Models.WikiLink>(links);
        merged.AddRange(extra);
        return merged;
    }

    private static IReadOnlyList<Wiki.Core.Models.WikiLink> AppendMarkdownImages(
        string text, MarkdownDocument doc, IReadOnlyList<Wiki.Core.Models.WikiLink> links)
    {
        List<Wiki.Core.Models.WikiLink>? extra = null;
        foreach (var node in doc.Descendants())
        {
            if (node is not Markdig.Syntax.Inlines.LinkInline { IsImage: true } img) continue;
            string? url = img.Url;
            if (string.IsNullOrWhiteSpace(url)) continue;
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;   // offline only
            if (!Wiki.Core.Embedding.ImageExtensions.IsImage(url)) continue;
            int start = img.Span.Start;
            int end = img.Span.End + 1;                                   // Markdig End is inclusive
            if (start < 0 || end > text.Length || start >= end) continue;
            // The alt text rides in the alias slot — like a wiki embed's |alias — so `![photo|300](p.png)`
            // carries its size hint to the renderer.
            string? alt = (img.FirstChild as Markdig.Syntax.Inlines.LiteralInline)?.Content.ToString();
            (extra ??= new()).Add(new Wiki.Core.Models.WikiLink(
                url, null, string.IsNullOrWhiteSpace(alt) ? null : alt, true, start, end));
        }
        if (extra is null) return links;
        var merged = new List<Wiki.Core.Models.WikiLink>(links);
        merged.AddRange(extra);
        return merged;
    }
}
