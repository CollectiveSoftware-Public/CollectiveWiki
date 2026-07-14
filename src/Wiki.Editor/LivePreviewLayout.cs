// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Editor;
using Wiki.Core.Embedding;
using Wiki.Core.Models;

namespace Wiki.Editor;

/// <summary>Pure: assembles a note (its line texts + decoration plan + links + front-matter) into the
/// flat list of <see cref="EditorRow"/>s the surface draws. Front-matter collapses to a single
/// <see cref="RowKind.Properties"/> row unless the caret is inside it (then those lines render raw and
/// editable); an unrevealed image-only line becomes an <see cref="RowKind.Image"/> row; the caret's line
/// reveals raw text; every other line hides its markers.</summary>
public static class LivePreviewLayout
{
    public static IReadOnlyList<EditorRow> Build(
        IReadOnlyList<string> lineTexts,
        IReadOnlyList<int> lineStarts,
        DecorationPlan plan,
        IReadOnlyList<WikiLink> links,
        FrontmatterBlock? frontmatter,
        Func<string, Code.Core.Abstractions.ISyntaxHighlighter>? codeHighlighter = null)
    {
        var rows = new List<EditorRow>();
        int line = 0;

        // Fenced code pre-pass: for each ``` … ``` block, tokenize its inner lines (lexer state carried across
        // lines for multi-line comments/strings) into per-line StyledRun lists. Fence lines render normally.
        var codeRuns = new IReadOnlyList<StyledRun>?[lineTexts.Count];
        if (codeHighlighter is not null)
            for (int l = 0; l < lineTexts.Count;)
            {
                if (lineTexts[l].TrimStart().StartsWith("```"))
                {
                    string lang = lineTexts[l].TrimStart()[3..].Trim();
                    var hl = codeHighlighter(lang);
                    int end = l + 1; int state = 0;
                    while (end < lineTexts.Count && !lineTexts[end].TrimStart().StartsWith("```"))
                    {
                        var tok = hl.Tokenize(lineTexts[end], state);
                        codeRuns[end] = BuildCodeRuns(lineTexts[end], tok.Tokens);
                        state = tok.EndState;
                        end++;
                    }
                    l = end + 1;
                }
                else l++;
            }

        // Precompute callout membership: a run of contiguous blockquote lines whose first line is a
        // callout header (> [!type] …) is a callout block; every line in it carries that CalloutInfo, and
        // the header line is flagged so it renders just the title.
        var calloutOf = new CalloutInfo?[lineTexts.Count];
        var calloutHeader = new bool[lineTexts.Count];
        for (int l = 0; l < lineTexts.Count;)
        {
            if (CalloutParser.DetectHeader(lineTexts[l]) is { } info)
            {
                int end = l;
                while (end + 1 < lineTexts.Count && lineTexts[end + 1].TrimStart().StartsWith(">")) end++;
                for (int k = l; k <= end; k++) calloutOf[k] = info;
                calloutHeader[l] = true;
                l = end + 1;
            }
            else l++;
        }

        if (frontmatter is not null && frontmatter.EndLine < plan.Lines.Count)
        {
            bool anyRevealed = false;
            for (int l = frontmatter.StartLine; l <= frontmatter.EndLine; l++)
                if (plan.Lines[l].RevealSource) { anyRevealed = true; break; }

            if (!anyRevealed)
            {
                rows.Add(new EditorRow(RowKind.Properties, frontmatter.StartLine, frontmatter.EndLine,
                    lineStarts[frontmatter.StartLine], false,
                    Array.Empty<StyledRun>(), Array.Empty<int>(), null, frontmatter.Entries));
                line = frontmatter.EndLine + 1;
            }
        }

        for (; line < lineTexts.Count; line++)
        {
            if (MarkdownTable.TryParse(lineTexts, line, out var table))
            {
                bool anyRevealed = false;
                for (int l = table.StartLine; l <= table.EndLine; l++)
                    if (plan.Lines[l].RevealSource) { anyRevealed = true; break; }

                if (!anyRevealed)
                {
                    rows.Add(new EditorRow(RowKind.Table, table.StartLine, table.EndLine,
                        lineStarts[table.StartLine], false, Array.Empty<StyledRun>(), Array.Empty<int>(),
                        null, null, table));
                    line = table.EndLine;   // for-loop ++ steps past the block
                    continue;
                }

                // Caret is inside the table → reveal every table line raw so the whole thing is editable.
                for (int l = table.StartLine; l <= table.EndLine; l++)
                {
                    string lt = lineTexts[l];
                    int s = lineStarts[l];
                    int en = s + lt.Length;
                    var sp = plan.Lines[l].Spans;
                    var ll = links.Where(k => k.SourceStart < en && k.SourceEnd > s).ToList();
                    var rr = RevealedLineBuilder.Build(lt, s, sp, ll);
                    var mp = new int[lt.Length + 1];
                    for (int k = 0; k <= lt.Length; k++) mp[k] = s + k;
                    rows.Add(new EditorRow(RowKind.Text, l, l, s, true, rr, mp, null, null));
                }
                line = table.EndLine;
                continue;
            }

            string text = lineTexts[line];
            int ls = lineStarts[line];
            int le = ls + text.Length;
            var spans = plan.Lines[line].Spans;
            bool reveal = plan.Lines[line].RevealSource;
            var lineLinks = links.Where(l => l.SourceStart < le && l.SourceEnd > ls).ToList();

            var callout = calloutOf[line];

            if (!reveal)
            {
                // A fenced code line (not the caret line): draw its syntax-highlighted token runs verbatim.
                if (codeRuns[line] is { } cruns)
                {
                    var cmap = new int[text.Length + 1];
                    for (int k = 0; k <= text.Length; k++) cmap[k] = ls + k;   // code is verbatim → identity map
                    rows.Add(new EditorRow(RowKind.Text, line, line, ls, false, cruns, cmap, null, null));
                    continue;
                }

                // A callout header line renders just its title (the "[!type]" marker is hidden), styled bold.
                if (callout is { } ci && calloutHeader[line])
                {
                    var titleRuns = new StyledRun[] { new(ci.Title, RunStyle.Bold) };
                    var tmap = new int[ci.Title.Length + 1];
                    for (int k = 0; k <= ci.Title.Length; k++) tmap[k] = ls;   // click anywhere → line start (reveals raw)
                    rows.Add(new EditorRow(RowKind.Text, line, line, ls, false, titleRuns, tmap, null, null, Callout: ci));
                    continue;
                }

                WikiLink? img = null;
                foreach (var l in lineLinks)
                    if (IsImageOnlyLine(text, ls, l)) { img = l; break; }
                if (img is not null)
                {
                    var (iw, ih) = ImageSizeHint.Parse(img.Alias);   // ![[pic.png|300]] / ![alt|300](pic.png)
                    rows.Add(new EditorRow(RowKind.Image, line, line, ls, false,
                        Array.Empty<StyledRun>(), Array.Empty<int>(), img.Target, null,
                        ImageWidth: iw, ImageHeight: ih));
                    continue;
                }

                var rl = RenderedLineBuilder.Build(text, ls, spans, lineLinks);
                if (rl.Runs.Count == 1 && rl.Runs[0].Style == RunStyle.Rule)
                    rows.Add(new EditorRow(RowKind.Rule, line, line, ls, false,
                        rl.Runs, rl.DisplayToRaw, null, null, Callout: callout));
                else
                    rows.Add(new EditorRow(RowKind.Text, line, line, ls, false,
                        rl.Runs, rl.DisplayToRaw, null, null, Callout: callout));
            }
            else
            {
                var runs = RevealedLineBuilder.Build(text, ls, spans, lineLinks);
                var map = new int[text.Length + 1];
                for (int k = 0; k <= text.Length; k++) map[k] = ls + k;
                rows.Add(new EditorRow(RowKind.Text, line, line, ls, true, runs, map, null, null, Callout: callout));
            }
        }

        return rows;
    }

    // Map the highlighter's tokens over one line into contiguous StyledRuns (gaps are plain Code runs).
    private static IReadOnlyList<StyledRun> BuildCodeRuns(
        string line, IReadOnlyList<Code.Core.Abstractions.StyledToken> tokens)
    {
        var runs = new List<StyledRun>();
        int pos = 0;
        foreach (var t in tokens.OrderBy(t => t.Start))
        {
            if (t.Start > pos) runs.Add(new StyledRun(line[pos..Math.Min(t.Start, line.Length)], RunStyle.Code));
            int end = Math.Min(line.Length, t.Start + t.Length);
            int start = Math.Min(t.Start, line.Length);
            if (end > start) runs.Add(new StyledRun(line[start..end], StyleFor(t.Kind)));
            pos = Math.Max(pos, end);
        }
        if (pos < line.Length) runs.Add(new StyledRun(line[pos..], RunStyle.Code));
        if (runs.Count == 0) runs.Add(new StyledRun(line, RunStyle.Code));
        return runs;
    }

    private static RunStyle StyleFor(Code.Core.Abstractions.TokenKind k) => k switch
    {
        Code.Core.Abstractions.TokenKind.Keyword or Code.Core.Abstractions.TokenKind.Preprocessor => RunStyle.CodeKeyword,
        Code.Core.Abstractions.TokenKind.String => RunStyle.CodeString,
        Code.Core.Abstractions.TokenKind.Comment => RunStyle.CodeComment,
        Code.Core.Abstractions.TokenKind.Number => RunStyle.CodeNumber,
        Code.Core.Abstractions.TokenKind.Type => RunStyle.CodeType,
        _ => RunStyle.Code
    };

    // A line that is nothing but a single image embed (whitespace allowed around it).
    private static bool IsImageOnlyLine(string lineText, int lineStart, WikiLink l)
    {
        if (!l.IsEmbed || !ImageExtensions.IsImage(l.Target)) return false;
        int ls = l.SourceStart - lineStart;
        int le = l.SourceEnd - lineStart;
        if (ls < 0 || le > lineText.Length || ls >= le) return false;
        return lineText[..ls].Trim().Length == 0 && lineText[le..].Trim().Length == 0;
    }
}
