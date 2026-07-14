// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Editor;
using Wiki.Core.Models;

namespace Wiki.Editor;

/// <summary>Pure: the runs for the caret's (revealed) line. The text is shown **raw and intact** (so it
/// stays editable, display == source), but markdown markers are styled <see cref="RunStyle.Marker"/>
/// (drawn greyed) while the content between them keeps its real style. So on the line you're editing,
/// <c>**bold**</c> shows a quiet grey <c>**</c>, a bold <c>bold</c>, a quiet grey <c>**</c>.</summary>
public static class RevealedLineBuilder
{
    public static IReadOnlyList<StyledRun> Build(
        string lineText, int lineStart, IReadOnlyList<DecorationSpan> spans, IReadOnlyList<WikiLink> links)
    {
        if (lineText.Length == 0)
            return new[] { new StyledRun("", RunStyle.Normal) };

        int lineEnd = lineStart + lineText.Length;
        SpanKind block = InlineDecor.BlockKind(spans, lineStart, lineEnd);
        int headingLevel = InlineDecor.HeadingLevel(block);
        RunStyle baseStyle = headingLevel > 0 ? RunStyle.Heading
            : block == SpanKind.Quote ? RunStyle.Quote
            : RunStyle.Normal;

        var (contentStart, _) = block == SpanKind.HorizontalRule
            ? (0, (string?)null)
            : InlineDecor.StripBlockMarker(lineText, block, headingLevel);
        bool[] marker = InlineDecor.MarkerMask(lineText, lineStart, spans);

        // Per-character style for the whole raw line.
        var styles = new RunStyle[lineText.Length];
        for (int i = 0; i < lineText.Length; i++)
        {
            int abs = lineStart + i;
            if (i < contentStart) styles[i] = RunStyle.Marker;                 // leading block marker
            else if (InlineDecor.InWikiLink(abs, spans)) styles[i] = RunStyle.WikiLink;  // raw [[..]] in link colour
            else if (marker[i]) styles[i] = RunStyle.Marker;                   // inline delimiter
            else styles[i] = InlineDecor.StyleAt(abs, spans, baseStyle);
        }

        // Collapse equal-styled neighbours into runs.
        var runs = new List<StyledRun>();
        int start = 0;
        for (int i = 1; i <= lineText.Length; i++)
        {
            if (i == lineText.Length || styles[i] != styles[start])
            {
                RunStyle st = styles[start];
                runs.Add(new StyledRun(lineText[start..i], st, st == RunStyle.Heading ? headingLevel : 0));
                start = i;
            }
        }
        return runs;
    }
}
