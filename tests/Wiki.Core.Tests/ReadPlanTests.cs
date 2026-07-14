// SPDX-License-Identifier: GPL-3.0-or-later
using Code.Core.Text;
using Wiki.Core.Editor;
using Wiki.Core.Parsing;

namespace Wiki.Core.Tests;

public class ReadPlanTests
{
    [Fact]
    public void Read_plan_reveals_no_lines()
    {
        var doc = new TextDocument("# H\n- item\ntext");
        var plan = new EditorModel(new MarkdigMarkdownParser()).ComputeReadPlan(doc);
        Assert.Equal(3, plan.Lines.Count);
        Assert.All(plan.Lines, l => Assert.False(l.RevealSource));
    }

    [Fact]
    public void Read_plan_still_carries_decoration_spans_and_widgets()
    {
        var doc = new TextDocument("# H\n![[pic.png]]");
        var plan = new EditorModel(new MarkdigMarkdownParser()).ComputeReadPlan(doc);
        Assert.Contains(plan.Lines, l => l.Spans.Count > 0);        // the heading still decorates
        Assert.Contains(plan.Widgets, w => w.Target == "pic.png");  // the embed still anchors
    }
}
