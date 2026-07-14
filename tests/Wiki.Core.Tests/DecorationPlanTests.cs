// SPDX-License-Identifier: GPL-3.0-or-later
using Code.Core.Text;
using Wiki.Core.Editor;
using Wiki.Core.Parsing;

namespace Wiki.Core.Tests;

public class DecorationPlanTests
{
    private static DecorationPlan Plan(string text, int caretOffset = -1)
    {
        var doc = new TextDocument(text);
        // Default caret at end-of-doc keeps line 0 unrevealed for single-line tests when caretOffset omitted
        var sel = new SelectionSet(Selection.At(caretOffset < 0 ? text.Length : caretOffset));
        return new EditorModel(new MarkdigMarkdownParser()).ComputePlan(doc, sel);
    }

    private static bool LineHas(DecorationPlan plan, int line, SpanKind kind)
        => plan.Lines.Any(l => l.LineIndex == line && l.Spans.Any(s => s.Kind == kind));

    [Fact]
    public void Plan_has_one_line_decoration_per_document_line()
    {
        var plan = Plan("a\nb\nc");
        Assert.Equal(3, plan.Lines.Count);
        Assert.Equal(new[] { 0, 1, 2 }, plan.Lines.Select(l => l.LineIndex).ToArray());
    }

    [Fact]
    public void Heading_levels_are_decorated()
    {
        Assert.True(LineHas(Plan("# H1\n\ntext", caretOffset: 100), 0, SpanKind.Heading1));
        Assert.True(LineHas(Plan("### H3\n\ntext", caretOffset: 100), 0, SpanKind.Heading3));
    }

    [Fact]
    public void Bold_italic_inline_code_links_are_decorated()
    {
        Assert.True(LineHas(Plan("a **b** c", caretOffset: 100), 0, SpanKind.Bold));
        Assert.True(LineHas(Plan("a *b* c", caretOffset: 100), 0, SpanKind.Italic));
        Assert.True(LineHas(Plan("a `b` c", caretOffset: 100), 0, SpanKind.InlineCode));
        Assert.True(LineHas(Plan("a [x](http://e) c", caretOffset: 100), 0, SpanKind.Link));
        Assert.True(LineHas(Plan("a [[Note]] c", caretOffset: 100), 0, SpanKind.WikiLink));
    }

    [Fact]
    public void Highlight_and_strikethrough_are_decorated()
    {
        // EmphasisExtras (Marked + Strikethrough) is enabled in the pipeline: `==x==` / `~~x~~` parse to
        // EmphasisInline nodes the DecorationBuilder maps by delimiter char, not the bold/italic count.
        Assert.True(LineHas(Plan("a ==b== c", caretOffset: 100), 0, SpanKind.Highlight));
        Assert.True(LineHas(Plan("a ~~b~~ c", caretOffset: 100), 0, SpanKind.Strikethrough));
    }

    [Fact]
    public void Lists_quotes_rules_and_fenced_code_are_decorated()
    {
        Assert.True(LineHas(Plan("- item\n- two", caretOffset: 100), 0, SpanKind.ListItem));
        Assert.True(LineHas(Plan("> quote", caretOffset: 100), 0, SpanKind.Quote));
        Assert.True(LineHas(Plan("---\n\ntext", caretOffset: 100), 0, SpanKind.HorizontalRule));
        Assert.True(LineHas(Plan("```\ncode\n```", caretOffset: 100), 1, SpanKind.CodeBlock));
    }
}
