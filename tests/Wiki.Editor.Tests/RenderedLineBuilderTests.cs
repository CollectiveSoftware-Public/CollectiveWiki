// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Editor;
using Wiki.Core.Models;
using Wiki.Core.Parsing;
using Wiki.Editor;
using Xunit;

namespace Wiki.Editor.Tests;

public class RenderedLineBuilderTests
{
    private static RenderedLine Line(string text)
    {
        var parser = new MarkdigMarkdownParser();
        var ast = parser.Parse(text);
        var spans = DecorationProbe.BuildSpans(ast);
        return RenderedLineBuilder.Build(text, 0, spans, ast.Links);
    }

    private static string Display(RenderedLine rl) => string.Concat(rl.Runs.Select(r => r.Text));

    [Fact]
    public void Bold_markers_are_hidden()
    {
        var rl = Line("a **b** c");
        Assert.DoesNotContain('*', Display(rl));
        Assert.Contains(rl.Runs, r => r.Style == RunStyle.Bold && r.Text == "b");
        Assert.Equal("a b c", Display(rl));
    }

    [Fact]
    public void Italic_markers_are_hidden()
    {
        var rl = Line("x *y* z");
        Assert.DoesNotContain('*', Display(rl));
        Assert.Contains(rl.Runs, r => r.Style == RunStyle.Italic && r.Text == "y");
    }

    [Fact]
    public void Inline_code_backticks_are_hidden()
    {
        var rl = Line("p `q` r");
        Assert.DoesNotContain('`', Display(rl));
        Assert.Contains(rl.Runs, r => r.Style == RunStyle.Code && r.Text == "q");
    }

    [Fact]
    public void Heading_marker_stripped()
    {
        var rl = Line("## Sub");
        var run = Assert.Single(rl.Runs);
        Assert.Equal("Sub", run.Text);
        Assert.Equal(RunStyle.Heading, run.Style);
        Assert.Equal(2, run.HeadingLevel);
    }

    [Fact]
    public void Display_to_raw_is_monotonic_and_maps_content_offset()
    {
        var rl = Line("a **b** c");
        // monotonic non-decreasing
        for (int i = 1; i < rl.DisplayToRaw.Length; i++)
            Assert.True(rl.DisplayToRaw[i] >= rl.DisplayToRaw[i - 1]);
        // display "a b c": 'b' at display index 2 maps to raw offset 4 (the 'b' in "a **b** c")
        Assert.Equal(4, rl.DisplayToRaw[2]);
        // trailing entry is the line end
        Assert.Equal("a **b** c".Length, rl.DisplayToRaw[^1]);
    }

    [Fact]
    public void Image_embed_is_an_image_run()
    {
        var rl = Line("![[Vista.png]]");
        var img = Assert.Single(rl.Runs, r => r.Style == RunStyle.Image);
        Assert.Equal("Vista.png", img.LinkTarget);
    }

    [Fact]
    public void Unordered_list_item_renders_a_bullet()
    {
        var rl = Line("- Milk");
        Assert.Contains(rl.Runs, r => r.Style == RunStyle.ListMarker && r.Text == "• ");
        Assert.Equal("• Milk", Display(rl));
    }

    [Fact]
    public void Ordered_list_item_keeps_its_number()
    {
        var rl = Line("1. First");
        Assert.Contains(rl.Runs, r => r.Style == RunStyle.ListMarker && r.Text == "1. ");
        Assert.Equal("1. First", Display(rl));
        Assert.DoesNotContain('•', Display(rl));
    }

    [Fact]
    public void Ordered_list_preserves_multidigit_number_and_paren_delimiter()
    {
        Assert.Contains(Line("10. Tenth").Runs, r => r.Style == RunStyle.ListMarker && r.Text == "10. ");
        Assert.Contains(Line("2) Second").Runs, r => r.Style == RunStyle.ListMarker && r.Text == "2) ");
    }

    [Fact]
    public void List_marker_map_has_one_entry_per_displayed_glyph()
    {
        var rl = Line("1. First");
        Assert.Equal(Display(rl).Length + 1, rl.DisplayToRaw.Length);   // +1 for the trailing line-end entry
    }
}
