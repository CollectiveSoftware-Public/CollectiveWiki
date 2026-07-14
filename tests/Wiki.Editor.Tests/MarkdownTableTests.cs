// SPDX-License-Identifier: GPL-3.0-or-later
using Xunit;

namespace Wiki.Editor.Tests;

public class MarkdownTableTests
{
    private static readonly string[] Table =
    {
        "| Name | Qty |",
        "| :--- | ---: |",
        "| Apple | 3 |",
        "| Pear | 12 |",
        "after",
    };

    [Fact]
    public void Parses_header_aligns_and_body_rows()
    {
        Assert.True(MarkdownTable.TryParse(Table, 0, out var m));
        Assert.Equal(new[] { "Name", "Qty" }, m.Headers);
        Assert.Equal(2, m.Rows.Count);
        Assert.Equal(new[] { "Apple", "3" }, m.Rows[0]);
        Assert.Equal(TableAlign.Left, m.Aligns[0]);
        Assert.Equal(TableAlign.Right, m.Aligns[1]);
        Assert.Equal(0, m.StartLine);
        Assert.Equal(3, m.EndLine);            // stops before "after"
    }

    [Fact]
    public void Rejects_a_non_table_line()
        => Assert.False(MarkdownTable.TryParse(new[] { "plain text", "more" }, 0, out _));

    [Fact]
    public void Rejects_when_the_delimiter_row_is_missing()
        => Assert.False(MarkdownTable.TryParse(new[] { "| a | b |", "| c | d |" }, 0, out _));
}
