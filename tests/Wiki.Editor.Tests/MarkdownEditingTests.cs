// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Editor;
using Xunit;

namespace Wiki.Editor.Tests;

public class MarkdownEditingTests
{
    // ---- inline wrap / unwrap ----

    [Fact]
    public void Wrap_selection_in_emphasis_keeps_the_text_selected()
    {
        var r = MarkdownEditing.Wrap("a b c", 2, 3, "*", "*");   // select "b"
        Assert.Equal("a *b* c", r.Text);
        Assert.Equal("b", r.Text[r.SelStart..r.SelEnd]);
    }

    [Fact]
    public void Wrap_empty_selection_places_caret_between_delimiters()
    {
        var r = MarkdownEditing.Wrap("ab", 1, 1, "**", "**");
        Assert.Equal("a****b", r.Text);
        Assert.Equal(3, r.SelStart);
        Assert.Equal(3, r.SelEnd);
    }

    [Fact]
    public void Wrap_unwraps_when_delimiters_are_inside_the_selection()
    {
        var r = MarkdownEditing.Wrap("a**b**c", 1, 6, "**", "**");   // select "**b**"
        Assert.Equal("abc", r.Text);
        Assert.Equal("b", r.Text[r.SelStart..r.SelEnd]);
    }

    [Fact]
    public void Wrap_unwraps_when_delimiters_are_just_outside_the_selection()
    {
        var r = MarkdownEditing.Wrap("a**b**c", 3, 4, "**", "**");   // select just "b"
        Assert.Equal("abc", r.Text);
        Assert.Equal("b", r.Text[r.SelStart..r.SelEnd]);
    }

    [Fact]
    public void Wrap_supports_asymmetric_delimiters_for_wikilinks()
    {
        var r = MarkdownEditing.Wrap("see Home now", 4, 8, "[[", "]]");   // select "Home"
        Assert.Equal("see [[Home]] now", r.Text);
        Assert.Equal("Home", r.Text[r.SelStart..r.SelEnd]);
    }

    // ---- line prefixes ----

    [Fact]
    public void Bullet_prefix_is_added_to_every_selected_line()
    {
        var r = MarkdownEditing.ToggleLinePrefix("foo\nbar", 0, 7, "- ");
        Assert.Equal("- foo\n- bar", r.Text);
    }

    [Fact]
    public void Bullet_prefix_toggles_off_when_all_lines_already_have_it()
    {
        var r = MarkdownEditing.ToggleLinePrefix("- foo\n- bar", 0, 11, "- ");
        Assert.Equal("foo\nbar", r.Text);
    }

    [Fact]
    public void Quote_prefix_added_to_a_single_line_with_a_collapsed_caret()
    {
        var r = MarkdownEditing.ToggleLinePrefix("hello", 2, 2, "> ");
        Assert.Equal("> hello", r.Text);
    }

    // ---- ordered list ----

    [Fact]
    public void Numbered_list_numbers_each_line_sequentially()
    {
        var r = MarkdownEditing.ToggleOrderedList("a\nb\nc", 0, 5);
        Assert.Equal("1. a\n2. b\n3. c", r.Text);
    }

    [Fact]
    public void Numbered_list_toggles_off_when_all_lines_are_numbered()
    {
        var r = MarkdownEditing.ToggleOrderedList("1. a\n2. b", 0, 8);
        Assert.Equal("a\nb", r.Text);
    }

    // ---- headings ----

    [Fact]
    public void Heading_is_applied_to_a_plain_line()
    {
        var r = MarkdownEditing.SetHeading("Title", 0, 0, 2);
        Assert.Equal("## Title", r.Text);
    }

    [Fact]
    public void Heading_replaces_an_existing_level()
    {
        var r = MarkdownEditing.SetHeading("# Title", 0, 0, 3);
        Assert.Equal("### Title", r.Text);
    }

    [Fact]
    public void Heading_clicked_at_its_current_level_toggles_off()
    {
        var r = MarkdownEditing.SetHeading("## Title", 0, 0, 2);
        Assert.Equal("Title", r.Text);
    }

    // ---- fenced code ----

    [Fact]
    public void Fence_wraps_the_selected_lines_and_selects_the_inner_text()
    {
        var r = MarkdownEditing.FenceBlock("code", 0, 4);
        Assert.Equal("```\ncode\n```", r.Text);
        Assert.Equal("code", r.Text[r.SelStart..r.SelEnd]);
    }

    // ---- image embed (paste) ----

    [Fact]
    public void Image_embed_on_an_empty_doc_gets_no_leading_newline_and_caret_lands_below()
    {
        var r = MarkdownEditing.InsertImageEmbed("", 0, 0, "Pasted image 20260708120000.png");
        Assert.Equal("![[Pasted image 20260708120000.png]]\n", r.Text);
        Assert.Equal(r.Text.Length, r.SelStart);   // caret on the blank line after the embed
        Assert.Equal(r.SelStart, r.SelEnd);
    }

    [Fact]
    public void Image_embed_mid_paragraph_becomes_its_own_line()
    {
        // Caret at the end of "Hello" — the embed must break onto its own line, not sit inline.
        var r = MarkdownEditing.InsertImageEmbed("Hello", 5, 5, "pic.png");
        Assert.Equal("Hello\n![[pic.png]]\n", r.Text);
    }

    [Fact]
    public void Image_embed_pushes_following_text_onto_the_next_line()
    {
        // Caret between "Hello " and "world" — the embed owns a line and "world" drops below it, so both the
        // embed line and the caret line ("world") are image-only / off the embed respectively.
        var r = MarkdownEditing.InsertImageEmbed("Hello world", 6, 6, "pic.png");
        Assert.Equal("Hello \n![[pic.png]]\nworld", r.Text);
        Assert.Equal("world", r.Text[r.SelStart..].TrimEnd('\n'));   // caret sits at the start of "world"
    }

    [Fact]
    public void Image_embed_at_a_line_start_omits_the_leading_newline()
    {
        var r = MarkdownEditing.InsertImageEmbed("a\nb", 2, 2, "pic.png");   // caret at start of line "b"
        Assert.Equal("a\n![[pic.png]]\nb", r.Text);
    }

    [Fact]
    public void Image_embed_replaces_the_active_selection()
    {
        var r = MarkdownEditing.InsertImageEmbed("drop this", 0, 4, "pic.png");   // "drop" selected
        Assert.Equal("![[pic.png]]\n this", r.Text);
    }

    // ---- Enter continues lists / quotes ----

    [Theory]
    [InlineData("- alpha", 7, "- alpha\n- ", 10)]                    // bullet continues
    [InlineData("* alpha", 7, "* alpha\n* ", 10)]
    [InlineData("  - alpha", 9, "  - alpha\n  - ", 14)]              // indent preserved
    [InlineData("3. alpha", 8, "3. alpha\n4. ", 12)]                 // ordered increments
    [InlineData("3) alpha", 8, "3) alpha\n4) ", 12)]                 // paren delimiter kept
    [InlineData("- [x] done", 10, "- [x] done\n- [ ] ", 17)]         // task continues unchecked
    [InlineData("> quote", 7, "> quote\n> ", 10)]                    // quote continues
    public void Continue_line_continues_the_marker(string text, int caret, string expected, int expCaret)
    {
        var r = MarkdownEditing.ContinueLine(text, caret);
        Assert.NotNull(r);
        Assert.Equal(expected, r!.Value.Text);
        Assert.Equal(expCaret, r.Value.SelStart);
        Assert.Equal(expCaret, r.Value.SelEnd);
    }

    [Fact]
    public void Continue_line_on_an_empty_item_ends_the_list()
    {
        // Enter on a marker-only line strips the marker (blank line), keeping the indent.
        var r = MarkdownEditing.ContinueLine("- alpha\n- ", 10);
        Assert.NotNull(r);
        Assert.Equal("- alpha\n", r!.Value.Text);
        Assert.Equal(8, r.Value.SelStart);
    }

    [Fact]
    public void Continue_line_splits_mid_line()
    {
        // Caret mid-content: the tail moves to the new item.
        var r = MarkdownEditing.ContinueLine("- alpha beta", 7);
        Assert.Equal("- alpha\n-  beta", r!.Value.Text);
        Assert.Equal(10, r.Value.SelStart);
    }

    [Theory]
    [InlineData("plain text", 10)]     // no marker
    [InlineData("- alpha", 1)]         // caret inside the marker
    public void Continue_line_returns_null_outside_a_list_context(string text, int caret)
        => Assert.Null(MarkdownEditing.ContinueLine(text, caret));

    // ---- Tab / Shift+Tab list indentation ----

    [Fact]
    public void Indent_lines_indents_a_list_line_keeping_the_caret()
    {
        var r = MarkdownEditing.IndentLines("- alpha", 3, 3);
        Assert.Equal("    - alpha", r!.Value.Text);
        Assert.Equal(7, r.Value.SelStart);   // caret shifted by one unit
    }

    [Fact]
    public void Indent_lines_returns_null_for_a_single_non_list_line()
        => Assert.Null(MarkdownEditing.IndentLines("plain", 3, 3));

    [Fact]
    public void Indent_lines_indents_every_line_of_a_multi_line_selection()
    {
        var r = MarkdownEditing.IndentLines("a\nb", 0, 3);
        Assert.Equal("    a\n    b", r!.Value.Text);
    }

    [Fact]
    public void Outdent_lines_strips_up_to_one_unit()
    {
        var r = MarkdownEditing.OutdentLines("    - alpha", 8, 8);
        Assert.Equal("- alpha", r!.Value.Text);
        Assert.Equal(4, r.Value.SelStart);
    }

    [Fact]
    public void Outdent_lines_returns_null_when_nothing_to_strip()
        => Assert.Null(MarkdownEditing.OutdentLines("- alpha", 3, 3));
}
