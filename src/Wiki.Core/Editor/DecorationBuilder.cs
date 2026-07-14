// SPDX-License-Identifier: GPL-3.0-or-later
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Wiki.Core.Models;
using Wiki.Core.Parsing;

namespace Wiki.Core.Editor;

/// <summary>Walks one <see cref="WikiAst"/> into a flat list of <see cref="DecorationSpan"/>. Pure;
/// no caret/line knowledge (the EditorModel assigns spans to lines and applies caret-reveal).</summary>
internal static class DecorationBuilder
{
    public static List<DecorationSpan> Build(WikiAst ast)
    {
        var spans = new List<DecorationSpan>();

        foreach (var node in ast.Document.Descendants())
        {
            switch (node)
            {
                case HeadingBlock h:
                    spans.Add(new DecorationSpan(h.Span.Start, h.Span.End + 1, HeadingKind(h.Level)));
                    break;
                case ThematicBreakBlock tb:
                    spans.Add(new DecorationSpan(tb.Span.Start, tb.Span.End + 1, SpanKind.HorizontalRule));
                    break;
                case QuoteBlock q:
                    spans.Add(new DecorationSpan(q.Span.Start, q.Span.End + 1, SpanKind.Quote));
                    break;
                case ListItemBlock li:
                    spans.Add(new DecorationSpan(li.Span.Start, li.Span.End + 1, SpanKind.ListItem));
                    break;
                case FencedCodeBlock fc:
                    spans.Add(new DecorationSpan(fc.Span.Start, fc.Span.End + 1, SpanKind.CodeBlock));
                    break;
                case CodeInline ci:
                    spans.Add(new DecorationSpan(ci.Span.Start, ci.Span.End + 1, SpanKind.InlineCode));
                    break;
                case EmphasisInline em:
                    // EmphasisExtras reuses EmphasisInline for `==mark==` (`=`) and `~~strike~~` (`~`);
                    // the `*`/`_` case keeps the bold-vs-italic count test.
                    spans.Add(new DecorationSpan(em.Span.Start, em.Span.End + 1, em.DelimiterChar switch
                    {
                        '=' => SpanKind.Highlight,
                        '~' => SpanKind.Strikethrough,
                        _ => em.DelimiterCount >= 2 ? SpanKind.Bold : SpanKind.Italic,
                    }));
                    break;
                case LinkInline link:
                    spans.Add(new DecorationSpan(link.Span.Start, link.Span.End + 1, SpanKind.Link));
                    break;
            }
        }

        // Wiki links/embeds come from the scanner (absolute offsets already; SourceEnd is exclusive).
        foreach (WikiLink wl in ast.Links)
            spans.Add(new DecorationSpan(wl.SourceStart, wl.SourceEnd, wl.IsEmbed ? SpanKind.Embed : SpanKind.WikiLink));

        return spans;
    }

    private static SpanKind HeadingKind(int level) => level switch
    {
        1 => SpanKind.Heading1, 2 => SpanKind.Heading2, 3 => SpanKind.Heading3,
        4 => SpanKind.Heading4, 5 => SpanKind.Heading5, _ => SpanKind.Heading6
    };
}
