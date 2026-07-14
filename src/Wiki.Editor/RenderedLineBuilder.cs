// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Wiki.Core.Editor;
using Wiki.Core.Embedding;
using Wiki.Core.Models;

namespace Wiki.Editor;

/// <summary>The runs for one rendered (non-caret) line, plus a display→raw column map.
/// <paramref name="DisplayToRaw"/> has one entry per displayed character giving its absolute source
/// offset, plus a trailing entry for the line end — so a click on the rendered text maps back to a caret
/// offset (markers are hidden, so display columns ≠ source columns).</summary>
public sealed record RenderedLine(IReadOnlyList<StyledRun> Runs, int[] DisplayToRaw);

/// <summary>Pure: turns one rendered line into styled runs with markdown markers **stripped** (live-preview
/// look) — <c>**bold**</c> shows <c>bold</c> in bold, no asterisks. Wikilinks render as their
/// display text; image embeds become an <see cref="RunStyle.Image"/> run carrying the target.</summary>
public static class RenderedLineBuilder
{
    public static RenderedLine Build(
        string lineText, int lineStart, IReadOnlyList<DecorationSpan> spans, IReadOnlyList<WikiLink> links)
    {
        int lineEnd = lineStart + lineText.Length;
        if (lineText.Length == 0)
            return new RenderedLine(new[] { new StyledRun("", RunStyle.Normal) }, new[] { lineStart });

        SpanKind block = InlineDecor.BlockKind(spans, lineStart, lineEnd);
        if (block == SpanKind.HorizontalRule)
            return new RenderedLine(new[] { new StyledRun("", RunStyle.Rule) }, new[] { lineStart });

        int headingLevel = InlineDecor.HeadingLevel(block);
        RunStyle baseStyle = headingLevel > 0 ? RunStyle.Heading
            : block == SpanKind.Quote ? RunStyle.Quote
            : RunStyle.Normal;

        var (contentStart, listMarker) = InlineDecor.StripBlockMarker(lineText, block, headingLevel);
        bool[] marker = InlineDecor.MarkerMask(lineText, lineStart, spans);

        var runs = new List<StyledRun>();
        var map = new List<int>(lineText.Length + 1);

        // A GFM task item ("- [ ] …" / "- [x] …"): render a checkbox glyph instead of the bullet, and skip
        // the "[ ] " text. The checkbox run carries the absolute offset of the state char so a click can
        // toggle it. Detected only for unordered items (listMarker "• ").
        int contentStartAfterBox = contentStart;
        bool isTask = listMarker == "• "
            && contentStart + 3 < lineText.Length
            && lineText[contentStart] == '['
            && (lineText[contentStart + 1] is ' ' or 'x' or 'X')
            && lineText[contentStart + 2] == ']'
            && lineText[contentStart + 3] == ' ';

        if (isTask)
        {
            bool done = lineText[contentStart + 1] is 'x' or 'X';
            int stateOffset = lineStart + contentStart + 1;
            runs.Add(new StyledRun(done ? "☑ " : "☐ ", RunStyle.Checkbox, 0, stateOffset.ToString()));
            map.Add(lineStart); map.Add(lineStart);            // two glyph columns -> line start
            contentStartAfterBox = contentStart + 4;           // skip "[ ] "
        }
        else if (listMarker is not null)
        {
            runs.Add(new StyledRun(listMarker, RunStyle.ListMarker));   // "• " unordered, "1. " etc. ordered
            for (int k = 0; k < listMarker.Length; k++) map.Add(lineStart);   // each marker glyph -> line start
        }

        var pending = new StringBuilder();
        RunStyle pendingStyle = baseStyle;

        void Flush()
        {
            if (pending.Length == 0) return;
            runs.Add(new StyledRun(pending.ToString(), pendingStyle,
                pendingStyle == RunStyle.Heading ? headingLevel : 0));
            pending.Clear();
        }

        int i = contentStartAfterBox;
        while (i < lineText.Length)
        {
            int abs = lineStart + i;

            WikiLink? link = null;
            foreach (var l in links)
                if (l.SourceStart == abs && l.SourceEnd <= lineEnd) { link = l; break; }
            if (link is not null)
            {
                Flush();
                RunStyle linkStyle = link.IsEmbed && ImageExtensions.IsImage(link.Target)
                    ? RunStyle.Image : RunStyle.WikiLink;
                runs.Add(new StyledRun(link.DisplayText, linkStyle, 0, link.Target));
                // Map every displayed character of the link to the link's start (monotonic; a click
                // anywhere on the link lands the caret at its opening, which then reveals the line).
                for (int k = 0; k < link.DisplayText.Length; k++) map.Add(abs);
                i += link.SourceEnd - link.SourceStart;
                continue;
            }

            if (marker[i]) { i++; continue; }   // hidden delimiter

            RunStyle charStyle = InlineDecor.StyleAt(abs, spans, baseStyle);
            if (charStyle != pendingStyle) { Flush(); pendingStyle = charStyle; }
            pending.Append(lineText[i]);
            map.Add(abs);
            i++;
        }
        Flush();

        if (runs.Count == 0) runs.Add(new StyledRun("", RunStyle.Normal));
        map.Add(lineEnd);   // trailing: caret-after-last-glyph -> line end
        return new RenderedLine(runs, map.ToArray());
    }
}
