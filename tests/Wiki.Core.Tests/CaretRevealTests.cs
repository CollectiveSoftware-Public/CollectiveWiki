// SPDX-License-Identifier: GPL-3.0-or-later
using Code.Core.Text;
using Wiki.Core.Editor;
using Wiki.Core.Parsing;

namespace Wiki.Core.Tests;

public class CaretRevealTests
{
    private const string Doc = "# Title\n\nSome **bold** body.\n\n## Second\n";
    // Lines: 0 "# Title", 1 "", 2 "Some **bold** body.", 3 "", 4 "## Second", 5 ""

    private static DecorationPlan PlanWithCaretOn(int line)
    {
        var doc = new TextDocument(Doc);
        int offset = doc.GetLineStartOffset(line);
        return new EditorModel(new MarkdigMarkdownParser())
            .ComputePlan(doc, new SelectionSet(Selection.At(offset)));
    }

    private static bool Revealed(DecorationPlan plan, int line)
        => plan.Lines.Single(l => l.LineIndex == line).RevealSource;

    [Fact]
    public void Only_the_caret_line_reveals_source()
    {
        var plan = PlanWithCaretOn(0);
        Assert.True(Revealed(plan, 0));
        Assert.False(Revealed(plan, 2));
        Assert.False(Revealed(plan, 4));
    }

    [Fact]
    public void Moving_the_caret_moves_the_revealed_line()
    {
        var plan = PlanWithCaretOn(2);
        Assert.False(Revealed(plan, 0));
        Assert.True(Revealed(plan, 2));
    }

    [Fact]
    public void A_multiline_selection_reveals_every_touched_line()
    {
        var doc = new TextDocument(Doc);
        int start = doc.GetLineStartOffset(0);
        int end = doc.GetLineStartOffset(2) + 4;   // into line 2
        var plan = new EditorModel(new MarkdigMarkdownParser())
            .ComputePlan(doc, new SelectionSet(Selection.Range(start, end)));
        Assert.True(Revealed(plan, 0));
        Assert.True(Revealed(plan, 1));
        Assert.True(Revealed(plan, 2));
        Assert.False(Revealed(plan, 4));
    }
}
