// SPDX-License-Identifier: GPL-3.0-or-later
using Code.Core.Text;
using Wiki.Core.Parsing;

namespace Wiki.Core.Editor;

/// <summary>The pure live-preview engine. Given the buffer + selection, computes the decoration plan:
/// which spans render rich, and which lines reveal raw markup (the caret's lines). Stateless and
/// deterministic — the make-or-break editor logic, proven headlessly.</summary>
public sealed class EditorModel
{
    private readonly IMarkdownParser _parser;
    public EditorModel(IMarkdownParser parser) => _parser = parser;

    public DecorationPlan ComputePlan(TextDocument document, SelectionSet selection)
        => Compute(document, RevealedLines(document, selection));

    /// <summary>Reading view: no line reveals raw source — everything renders rich, caret or not.</summary>
    public DecorationPlan ComputeReadPlan(TextDocument document)
        => Compute(document, new HashSet<int>());

    private DecorationPlan Compute(TextDocument document, HashSet<int> revealLines)
    {
        string text = document.GetText();
        var ast = _parser.Parse(text);
        var spans = DecorationBuilder.Build(ast);

        int lineCount = document.LineCount;

        var lines = new List<LineDecoration>(lineCount);
        for (int line = 0; line < lineCount; line++)
        {
            int lineStart = document.GetLineStartOffset(line);
            int lineEnd = lineStart + document.GetLineLength(line);   // exclusive (excludes newline)
            var lineSpans = spans
                .Where(s => s.Start < lineEnd && s.End > lineStart)   // intersects this line
                .OrderBy(s => s.Start)
                .ToList();
            lines.Add(new LineDecoration(line, revealLines.Contains(line), lineSpans));
        }

        var widgets = ast.Links
            .Where(l => l.IsEmbed)
            .Select(l => new WidgetAnchor(l.SourceStart, ClassifyWidget(l.Target), l.Target))
            .ToList();

        return new DecorationPlan(lines, widgets);
    }

    private static WidgetKind ClassifyWidget(string target)
        => Embedding.ImageExtensions.IsImage(target) ? WidgetKind.Image : WidgetKind.Transclusion;

    // A line reveals raw source if any selection's caret or range touches it.
    private static HashSet<int> RevealedLines(TextDocument document, SelectionSet selection)
    {
        var revealed = new HashSet<int>();
        foreach (Selection s in selection.Selections)
        {
            int startLine = document.OffsetToPosition(Math.Min(s.Start, document.Length)).Line;
            int endLine = document.OffsetToPosition(Math.Min(s.End, document.Length)).Line;
            for (int line = startLine; line <= endLine; line++) revealed.Add(line);
        }
        return revealed;
    }
}
