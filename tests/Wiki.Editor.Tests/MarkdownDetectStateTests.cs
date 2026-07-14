// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Editor;
using Xunit;

namespace Wiki.Editor.Tests;

public class MarkdownDetectStateTests
{
    // ---- inline marks (detected when the selection is wrapped, in or out) ----

    [Fact]
    public void Bold_detected_when_delimiters_inside_selection()
    {
        // select "**b**"
        var f = MarkdownEditing.DetectState("a **b** c", 2, 7);
        Assert.True(f.Bold);
    }

    [Fact]
    public void Bold_detected_when_delimiters_outside_selection()
    {
        // select just "b" between the ** pairs
        var f = MarkdownEditing.DetectState("a **b** c", 4, 5);
        Assert.True(f.Bold);
    }

    [Fact]
    public void Italic_not_reported_as_bold_and_vice_versa()
    {
        var italic = MarkdownEditing.DetectState("a *b* c", 2, 5);   // select "*b*"
        Assert.True(italic.Italic);
        Assert.False(italic.Bold);

        var bold = MarkdownEditing.DetectState("a **b** c", 2, 7);
        Assert.True(bold.Bold);
        Assert.False(bold.Italic);
    }

    [Fact]
    public void Strike_and_code_detected()
    {
        Assert.True(MarkdownEditing.DetectState("~~x~~", 0, 5).Strike);
        Assert.True(MarkdownEditing.DetectState("`x`", 0, 3).Code);
    }

    [Fact]
    public void Highlight_detected_inside_and_outside_selection()
    {
        Assert.True(MarkdownEditing.DetectState("==x==", 0, 5).Highlight);       // "==x==" selected
        Assert.True(MarkdownEditing.DetectState("a ==b== c", 4, 5).Highlight);   // just "b" between the == pairs
        Assert.False(MarkdownEditing.DetectState("plain", 0, 5).Highlight);
    }

    [Fact]
    public void Collapsed_caret_reports_no_inline_marks()
    {
        var f = MarkdownEditing.DetectState("a **bold** c", 5, 5);   // caret inside "bold"
        Assert.False(f.Bold);
        Assert.False(f.Italic);
    }

    // ---- heading level from the caret's line ----

    [Theory]
    [InlineData("# Title", 1)]
    [InlineData("### Deep", 3)]
    [InlineData("no heading", 0)]
    public void Heading_level_read_from_caret_line(string line, int expected)
        => Assert.Equal(expected, MarkdownEditing.DetectState(line, 2, 2).HeadingLevel);

    [Fact]
    public void Heading_read_for_the_caret_line_in_a_multiline_doc()
    {
        const string doc = "intro\n## Section\nbody";
        int caret = doc.IndexOf("Section");
        var f = MarkdownEditing.DetectState(doc, caret, caret);
        Assert.Equal(2, f.HeadingLevel);
    }

    // ---- list kind from the caret's line ----

    [Fact]
    public void Bullet_line_detected()
    {
        var f = MarkdownEditing.DetectState("- item", 3, 3);
        Assert.True(f.Bullet);
        Assert.False(f.Numbered);
    }

    [Fact]
    public void Numbered_line_detected()
    {
        var f = MarkdownEditing.DetectState("1. item", 3, 3);
        Assert.True(f.Numbered);
        Assert.False(f.Bullet);
    }

    [Fact]
    public void Quote_line_is_neither_bullet_nor_numbered()
    {
        var f = MarkdownEditing.DetectState("> quote", 3, 3);
        Assert.False(f.Bullet);
        Assert.False(f.Numbered);
    }

    [Fact]
    public void Task_line_reads_as_bullet()
    {
        var f = MarkdownEditing.DetectState("- [ ] todo", 6, 6);
        Assert.True(f.Bullet);
    }
}
