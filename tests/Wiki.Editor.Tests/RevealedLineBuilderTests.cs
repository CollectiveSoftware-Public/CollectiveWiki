// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Editor;
using Wiki.Core.Parsing;
using Wiki.Editor;
using Xunit;

namespace Wiki.Editor.Tests;

public class RevealedLineBuilderTests
{
    private static IReadOnlyList<StyledRun> Line(string text)
    {
        var parser = new MarkdigMarkdownParser();
        var ast = parser.Parse(text);
        var spans = DecorationProbe.BuildSpans(ast);
        return RevealedLineBuilder.Build(text, 0, spans, ast.Links);
    }

    private static string Raw(IReadOnlyList<StyledRun> runs) => string.Concat(runs.Select(r => r.Text));

    [Fact]
    public void Raw_text_is_preserved_intact()
    {
        var runs = Line("a **b** c");
        Assert.Equal("a **b** c", Raw(runs));
    }

    [Fact]
    public void Emphasis_markers_are_greyed_content_is_bold()
    {
        var runs = Line("a **b** c");
        Assert.Contains(runs, r => r.Style == RunStyle.Marker && r.Text == "**");
        Assert.Contains(runs, r => r.Style == RunStyle.Bold && r.Text == "b");
    }

    [Fact]
    public void Heading_marker_greyed_content_is_heading()
    {
        var runs = Line("## Sub");
        Assert.Contains(runs, r => r.Style == RunStyle.Marker && r.Text.Contains("##"));
        Assert.Contains(runs, r => r.Style == RunStyle.Heading && r.Text == "Sub");
        Assert.Equal(6, Raw(runs).Length);
    }

    [Fact]
    public void Ordered_list_marker_stays_raw_and_greyed_on_the_caret_line()
    {
        // The caret's line shows the source intact (still editable) with the "1." marker region greyed.
        var runs = Line("1. First");
        Assert.Equal("1. First", Raw(runs));
        Assert.Contains(runs, r => r.Style == RunStyle.Marker && r.Text.Contains("1."));
    }
}
