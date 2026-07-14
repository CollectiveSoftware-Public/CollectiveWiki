// SPDX-License-Identifier: GPL-3.0-or-later
using Code.Core.Text;
using Wiki.Core.Editor;
using Wiki.Core.Parsing;
using Wiki.Editor;
using Xunit;

namespace Wiki.Editor.Tests;

public class LivePreviewLayoutTests
{
    private static IReadOnlyList<EditorRow> Lay(string text, int caret)
    {
        var doc = new TextDocument(text);
        var parser = new MarkdigMarkdownParser();
        var model = new EditorModel(parser);
        var plan = model.ComputePlan(doc, new SelectionSet(Selection.At(caret)));
        var ast = parser.Parse(text);
        var texts = new List<string>();
        var starts = new List<int>();
        for (int i = 0; i < doc.LineCount; i++) { texts.Add(doc.GetLineText(i)); starts.Add(doc.GetLineStartOffset(i)); }
        return LivePreviewLayout.Build(texts, starts, plan, ast.Links, FrontmatterParser.Parse(text));
    }

    private static IReadOnlyList<EditorRow> LayWithHighlighter(
        string text, int caret, Func<string, Code.Core.Abstractions.ISyntaxHighlighter> hl)
    {
        var doc = new TextDocument(text);
        var parser = new MarkdigMarkdownParser();
        var model = new EditorModel(parser);
        var plan = model.ComputePlan(doc, new SelectionSet(Selection.At(caret)));
        var ast = parser.Parse(text);
        var texts = new List<string>();
        var starts = new List<int>();
        for (int i = 0; i < doc.LineCount; i++) { texts.Add(doc.GetLineText(i)); starts.Add(doc.GetLineStartOffset(i)); }
        return LivePreviewLayout.Build(texts, starts, plan, ast.Links, FrontmatterParser.Parse(text), hl);
    }

    [Fact]
    public void Fenced_code_block_is_token_coloured_when_unrevealed()
    {
        const string note = "```cs\nint x = 1; // c\n```\nafter";
        var registry = new Code.Syntax.SyntaxHighlighterRegistry();
        Func<string, Code.Core.Abstractions.ISyntaxHighlighter> hl =
            lang => registry.ForFile(lang is "cs" or "csharp" ? "x.cs" : "x.txt");

        var rows = LayWithHighlighter(note, note.Length, hl);   // caret on "after" → block unrevealed
        var codeRow = rows.First(r => r.FirstLine == 1);        // the "int x = 1; // c" line
        Assert.Contains(codeRow.Runs, run => run.Style == RunStyle.CodeKeyword && run.Text == "int");
        Assert.Contains(codeRow.Runs, run => run.Style == RunStyle.CodeNumber && run.Text.Contains("1"));
        Assert.Contains(codeRow.Runs, run => run.Style == RunStyle.CodeComment && run.Text.Contains("//"));
    }

    [Fact]
    public void Unknown_fence_language_falls_back_to_plain_code_runs()
    {
        const string note = "```\nplain text line\n```\nx";
        var rows = LayWithHighlighter(note, note.Length, _ => Code.Syntax.PlainHighlighter.Instance);
        var codeRow = rows.First(r => r.FirstLine == 1);
        Assert.All(codeRow.Runs, run => Assert.True(
            run.Style is RunStyle.Code or RunStyle.Normal or RunStyle.CodeComment
            or RunStyle.CodeKeyword or RunStyle.CodeString or RunStyle.CodeNumber or RunStyle.CodeType));
    }

    private static string RowText(EditorRow r) => string.Concat(r.Runs.Select(x => x.Text));

    private const string FmNote = "---\ntype: index\nstatus: draft\n---\n# Body";

    [Fact]
    public void Frontmatter_collapses_to_a_properties_row_when_caret_in_body()
    {
        var rows = Lay(FmNote, FmNote.Length);   // caret on "# Body"
        var props = rows[0];
        Assert.Equal(RowKind.Properties, props.Kind);
        Assert.NotNull(props.Properties);
        Assert.Equal("type", props.Properties![0].Key);
        Assert.Equal("index", props.Properties[0].Value);
        Assert.Contains(rows, r => r.Kind == RowKind.Text);   // the body heading row
    }

    [Fact]
    public void Caret_inside_frontmatter_reveals_it_as_text_not_a_widget()
    {
        // caret on the "type: index" line (line 1)
        int caret = FmNote.IndexOf("type", System.StringComparison.Ordinal);
        var rows = Lay(FmNote, caret);
        Assert.DoesNotContain(rows, r => r.Kind == RowKind.Properties);
        Assert.Contains(rows, r => r.Kind == RowKind.Text && RowText(r).Contains("type: index"));
    }

    [Fact]
    public void Image_only_line_becomes_an_image_row_when_unrevealed()
    {
        const string note = "# H\n![[pic.png]]\nmore";
        var rows = Lay(note, 0);   // caret on line 0, image line unrevealed
        Assert.Contains(rows, r => r.Kind == RowKind.Image && r.ImageTarget == "pic.png");
    }

    [Fact]
    public void Image_row_carries_the_alias_size_hint()
    {
        var rows = Lay("# H\n![[pic.png|300]]\nmore", 0);
        var img = rows.Single(r => r.Kind == RowKind.Image);
        Assert.Equal("pic.png", img.ImageTarget);
        Assert.Equal(300, img.ImageWidth);
        Assert.Null(img.ImageHeight);
    }

    [Fact]
    public void Image_row_carries_a_width_x_height_hint()
    {
        var rows = Lay("x\n![[pic.png|320x180]]", 0);   // caret on line 0 keeps the image line unrevealed
        var img = rows.Single(r => r.Kind == RowKind.Image);
        Assert.Equal(320, img.ImageWidth);
        Assert.Equal(180, img.ImageHeight);
    }

    [Fact]
    public void Markdown_image_alt_carries_the_size_hint()
    {
        var rows = Lay("x\n![photo|250](pics/cat.png)", 0);
        var img = rows.Single(r => r.Kind == RowKind.Image);
        Assert.Equal("pics/cat.png", img.ImageTarget);
        Assert.Equal(250, img.ImageWidth);
    }

    [Fact]
    public void Read_plan_renders_the_caret_line_rich()
    {
        // Reading view: even the line the caret sits on renders as a widget, not raw markup.
        const string note = "![[pic.png]]";
        var doc = new TextDocument(note);
        var parser = new MarkdigMarkdownParser();
        var plan = new EditorModel(parser).ComputeReadPlan(doc);
        var ast = parser.Parse(note);
        var rows = LivePreviewLayout.Build(new[] { note }, new[] { 0 }, plan, ast.Links, null);
        Assert.Contains(rows, r => r.Kind == RowKind.Image && r.ImageTarget == "pic.png");
    }

    [Fact]
    public void Image_line_reveals_as_raw_text_when_caret_on_it()
    {
        const string note = "# H\n![[pic.png]]\nmore";
        int caret = note.IndexOf("![[", System.StringComparison.Ordinal) + 1;
        var rows = Lay(note, caret);
        Assert.DoesNotContain(rows, r => r.Kind == RowKind.Image);
        Assert.Contains(rows, r => r.Kind == RowKind.Text && RowText(r).Contains("![[pic.png]]"));
    }

    [Fact]
    public void Pasting_an_image_mid_paragraph_renders_as_an_image_not_a_raw_link()
    {
        // Reproduces the bug: an inline embed left on the caret's line shows raw '![[…]]' text. The paste
        // transform must block-place the embed and drop the caret below it so the surface draws the picture.
        const string before = "Notes about the trip";
        var paste = MarkdownEditing.InsertImageEmbed(before, before.Length, before.Length,
            "Pasted image 20260708120000.png");

        var rows = Lay(paste.Text, paste.SelStart);   // caret where the transform left it (below the embed)

        Assert.Contains(rows, r => r.Kind == RowKind.Image && r.ImageTarget == "Pasted image 20260708120000.png");
        Assert.DoesNotContain(rows, r => r.Kind == RowKind.Text && RowText(r).Contains("![["));
    }

    [Fact]
    public void Callout_block_tags_rows_and_renders_the_title()
    {
        const string note = "> [!warning] Heads up\n> body line\n\nafter";
        var rows = Lay(note, note.Length);   // caret on "after" → the callout is unrevealed
        var header = rows.First(r => r.FirstLine == 0);
        Assert.NotNull(header.Callout);
        Assert.Equal("amber", header.Callout!.Family);
        Assert.Equal("Heads up", RowText(header));       // title shown, the [!warning] marker hidden
        Assert.NotNull(rows.First(r => r.FirstLine == 1).Callout);   // body line still in the block
    }

    [Fact]
    public void Callout_line_reveals_raw_when_caret_inside()
    {
        const string note = "> [!note] Title\n> body\n\nafter";
        var rows = Lay(note, 3);   // caret inside the header line
        var header = rows.First(r => r.FirstLine == 0);
        Assert.True(header.Revealed);
        Assert.Contains("[!note]", RowText(header));      // raw shown for editing
        Assert.NotNull(header.Callout);                    // still tagged, so the box still draws
    }

    [Fact]
    public void Caret_line_reveals_raw_markers_other_lines_hide_them()
    {
        const string note = "**b**\nx";
        // caret on line 1 -> line 0 rendered (markers hidden)
        var hidden = Lay(note, note.Length).First(r => r.FirstLine == 0);
        Assert.DoesNotContain('*', RowText(hidden));
        // caret on line 0 -> reconstructs raw
        var shown = Lay(note, 0).First(r => r.FirstLine == 0);
        Assert.Equal("**b**", RowText(shown));
    }
}
