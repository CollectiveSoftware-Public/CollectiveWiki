// SPDX-License-Identifier: GPL-3.0-or-later
using Markdig.Syntax;
using Wiki.Core.Parsing;

namespace Wiki.Core.Embedding;

/// <summary>Finds the source range of a named heading's section, using the single Markdig AST. A
/// section spans from its heading to the next heading whose level is less than or equal to its own
/// (deeper subsections are included), or to end of document.</summary>
public sealed class HeadingSectionExtractor
{
    private readonly IMarkdownParser _parser;
    public HeadingSectionExtractor(IMarkdownParser parser) => _parser = parser;

    public NoteSection? Extract(string text, string headingTitle)
    {
        string wanted = headingTitle.Trim();
        var headings = _parser.Parse(text).Document.Descendants<HeadingBlock>()
            .Select(h => (h.Level, Start: h.Span.Start, Title: HeadingText(text, h)))
            .OrderBy(h => h.Start)
            .ToList();

        for (int idx = 0; idx < headings.Count; idx++)
        {
            var h = headings[idx];
            if (!string.Equals(h.Title, wanted, StringComparison.OrdinalIgnoreCase)) continue;

            int end = text.Length;
            for (int j = idx + 1; j < headings.Count; j++)
                if (headings[j].Level <= h.Level) { end = headings[j].Start; break; }

            return new NoteSection(h.Title, h.Level, h.Start, end);
        }
        return null;
    }

    // The heading's plain text: the heading line's source with leading '#'s and spaces stripped.
    private static string HeadingText(string text, HeadingBlock h)
    {
        int start = h.Span.Start;
        int end = Math.Min(h.Span.End + 1, text.Length);     // Markdig spans have an inclusive End
        string line = text[start..end];
        return line.TrimStart('#', ' ', '\t').Trim();
    }
}
