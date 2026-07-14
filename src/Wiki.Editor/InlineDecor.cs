// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Editor;

namespace Wiki.Editor;

/// <summary>Shared pure helpers for the rendered- and revealed-line builders: block-marker stripping,
/// inline emphasis/code delimiter location, and per-character content styling. All operate on one line's
/// raw text plus the <see cref="DecorationSpan"/>s that intersect it (absolute, <c>[Start,End)</c>).</summary>
internal static class InlineDecor
{
    /// <summary>The block kind governing this line (heading/quote/list/hr), or <see cref="SpanKind.Plain"/>.</summary>
    public static SpanKind BlockKind(IReadOnlyList<DecorationSpan> spans, int lineStart, int lineEnd)
        => spans.Where(s => s.Start < lineEnd && s.End > lineStart)
                .Select(s => s.Kind)
                .FirstOrDefault(k => k is SpanKind.Heading1 or SpanKind.Heading2 or SpanKind.Heading3
                    or SpanKind.Heading4 or SpanKind.Heading5 or SpanKind.Heading6
                    or SpanKind.Quote or SpanKind.ListItem or SpanKind.HorizontalRule);

    public static int HeadingLevel(SpanKind k) => k switch
    {
        SpanKind.Heading1 => 1, SpanKind.Heading2 => 2, SpanKind.Heading3 => 3,
        SpanKind.Heading4 => 4, SpanKind.Heading5 => 5, SpanKind.Heading6 => 6, _ => 0
    };

    /// <summary>Where inline content begins after the leading block marker (local index), and the glyph to
    /// render in its place for a list item — <c>"• "</c> for an unordered item, or the literal number+space
    /// (e.g. <c>"1. "</c>, <c>"10) "</c>) for an ordered one — else <c>null</c>. The whole [0, ContentStart)
    /// prefix is the block marker (the markers, indentation and trailing space).</summary>
    public static (int ContentStart, string? Marker) StripBlockMarker(string lineText, SpanKind block, int headingLevel)
    {
        int i = 0;
        int len = lineText.Length;
        if (headingLevel > 0)
        {
            while (i < len && lineText[i] == '#') i++;
            while (i < len && (lineText[i] == ' ' || lineText[i] == '\t')) i++;
            return (i, null);
        }
        if (block == SpanKind.Quote)
        {
            while (i < len && (lineText[i] == '>' || lineText[i] == ' ' || lineText[i] == '\t')) i++;
            return (i, null);
        }
        if (block == SpanKind.ListItem)
        {
            int j = i;
            while (j < len && (lineText[j] == ' ' || lineText[j] == '\t')) j++;
            int markStart = j;
            bool bullet = j < len && (lineText[j] == '-' || lineText[j] == '*' || lineText[j] == '+');
            if (bullet) j++;
            else while (j < len && char.IsDigit(lineText[j])) j++;
            if (j < len && (lineText[j] == '.' || lineText[j] == ')')) j++;
            if (j > markStart && j < len && lineText[j] == ' ')
                // Unordered → a bullet glyph; ordered → keep the real number/delimiter as typed.
                return (j + 1, bullet ? "• " : lineText[markStart..j] + " ");
        }
        return (0, null);
    }

    /// <summary>A mask over the line: true where a character is an inline emphasis/inline-code delimiter
    /// (<c>**</c>, <c>*</c>, <c>__</c>, <c>_</c>, <c>`</c>…) — i.e. a marker hidden on rendered lines. The
    /// delimiter run length is read from the raw text at each span's edges (robust to 1-vs-2 counts).</summary>
    public static bool[] MarkerMask(string lineText, int lineStart, IReadOnlyList<DecorationSpan> spans)
    {
        var mask = new bool[lineText.Length];
        foreach (var s in spans)
        {
            if (s.Kind is not (SpanKind.Bold or SpanKind.Italic or SpanKind.InlineCode
                or SpanKind.Highlight or SpanKind.Strikethrough)) continue;
            int ls = Math.Max(0, s.Start - lineStart);
            int le = Math.Min(lineText.Length, s.End - lineStart);
            if (ls >= le) continue;
            char d = lineText[ls];
            if (d is not ('*' or '_' or '`' or '=' or '~')) continue;
            int n = 0;
            while (ls + n < le && lineText[ls + n] == d) n++;
            n = Math.Min(n, (le - ls) / 2);
            for (int k = 0; k < n; k++) { mask[ls + k] = true; mask[le - 1 - k] = true; }
        }
        return mask;
    }

    /// <summary>The content style of one character: inline code/highlight/bold/italic/strike/link override
    /// the base style. A character carries a single <see cref="RunStyle"/> (as bold-vs-italic already do),
    /// so on overlap the precedence here decides — highlight outranks bold/italic so a `==**x**==` still
    /// shows its mark background.</summary>
    public static RunStyle StyleAt(int abs, IReadOnlyList<DecorationSpan> spans, RunStyle baseStyle)
    {
        bool code = false, bold = false, italic = false, link = false, highlight = false, strike = false;
        foreach (var s in spans)
        {
            if (s.Start <= abs && abs < s.End)
            {
                switch (s.Kind)
                {
                    case SpanKind.InlineCode or SpanKind.CodeBlock: code = true; break;
                    case SpanKind.Bold: bold = true; break;
                    case SpanKind.Italic: italic = true; break;
                    case SpanKind.Highlight: highlight = true; break;
                    case SpanKind.Strikethrough: strike = true; break;
                    case SpanKind.Link: link = true; break;
                }
            }
        }
        if (code) return RunStyle.Code;
        if (highlight) return RunStyle.Highlight;
        if (bold) return RunStyle.Bold;
        if (italic) return RunStyle.Italic;
        if (strike) return RunStyle.Strikethrough;
        if (link) return RunStyle.Link;
        return baseStyle;
    }

    /// <summary>True if the absolute offset lies inside a wikilink/embed span (handled specially).</summary>
    public static bool InWikiLink(int abs, IReadOnlyList<DecorationSpan> spans)
    {
        foreach (var s in spans)
            if ((s.Kind == SpanKind.WikiLink || s.Kind == SpanKind.Embed) && s.Start <= abs && abs < s.End)
                return true;
        return false;
    }
}
