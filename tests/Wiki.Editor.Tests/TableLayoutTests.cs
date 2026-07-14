// SPDX-License-Identifier: GPL-3.0-or-later
using Xunit;

namespace Wiki.Editor.Tests;

public class TableLayoutTests
{
    [Fact]
    public void Widest_cell_drives_the_column_width_and_total_fits()
    {
        MarkdownTable.TryParse(new[] { "| a | bbbb |", "| - | - |", "| cc | d |" }, 0, out var m);
        // measure = character count as a stand-in for pixel width
        var cols = TableLayout.Columns(m, s => s.Length, maxTotalWidth: 1000);
        Assert.Equal(2, cols.Length);
        Assert.True(cols[1] > cols[0]);            // "bbbb" wider than "a"/"cc"
        Assert.True(cols.Sum() <= 1000);
    }

    [Fact]
    public void Columns_are_scaled_down_to_fit_a_narrow_width()
    {
        MarkdownTable.TryParse(new[] { "| aaaaa | bbbbb |", "| - | - |" }, 0, out var m);
        var cols = TableLayout.Columns(m, s => s.Length * 100, maxTotalWidth: 300);
        Assert.True(cols.Sum() <= 300 + 0.01);
    }
}
