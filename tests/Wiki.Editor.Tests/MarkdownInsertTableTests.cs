// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Editor;
using Xunit;

namespace Wiki.Editor.Tests;

public class MarkdownInsertTableTests
{
    [Fact]
    public void Inserted_table_parses_back_through_MarkdownTable()
    {
        var r = MarkdownEditing.InsertTableBlock("", 0, 0, rows: 3, cols: 2);
        var lines = r.Text.Split('\n');
        Assert.True(MarkdownTable.TryParse(lines, 0, out var model));
        Assert.Equal(2, model.Headers.Count);
        Assert.Equal(new[] { "Column 1", "Column 2" }, model.Headers);
        Assert.Equal(2, model.Rows.Count);   // 3 rows total = header + 2 body
    }

    [Fact]
    public void Inserted_as_its_own_block_when_mid_line()
    {
        // caret after "x" (not a line start) → a leading newline separates the table
        var r = MarkdownEditing.InsertTableBlock("x", 1, 1, rows: 1, cols: 1);
        Assert.StartsWith("x\n|", r.Text);
        Assert.EndsWith("\n", r.Text);
    }

    [Fact]
    public void No_leading_newline_at_line_start()
    {
        var r = MarkdownEditing.InsertTableBlock("", 0, 0, rows: 2, cols: 3);
        Assert.StartsWith("|", r.Text);
        var lines = r.Text.Split('\n');
        Assert.True(MarkdownTable.TryParse(lines, 0, out var model));
        Assert.Equal(3, model.Headers.Count);
    }
}
