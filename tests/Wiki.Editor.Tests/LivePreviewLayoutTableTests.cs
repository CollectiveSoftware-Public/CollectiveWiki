// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Editor;
using Wiki.Core.Models;
using Xunit;

namespace Wiki.Editor.Tests;

public class LivePreviewLayoutTableTests
{
    private static (IReadOnlyList<string> texts, IReadOnlyList<int> starts) Lines(params string[] lines)
    {
        var starts = new List<int>();
        int off = 0;
        foreach (var l in lines) { starts.Add(off); off += l.Length + 1; }
        return (lines, starts);
    }

    private static DecorationPlan Plan(int lineCount, int revealLine = -1)
    {
        var ld = new List<LineDecoration>();
        for (int i = 0; i < lineCount; i++)
            ld.Add(new LineDecoration(i, i == revealLine, System.Array.Empty<DecorationSpan>()));
        return new DecorationPlan(ld, System.Array.Empty<WidgetAnchor>());
    }

    [Fact]
    public void Contiguous_table_with_no_caret_becomes_one_table_row()
    {
        var (texts, starts) = Lines("| A | B |", "| - | - |", "| 1 | 2 |", "end");
        var rows = LivePreviewLayout.Build(texts, starts, Plan(4), System.Array.Empty<WikiLink>(), null);
        var table = rows.SingleOrDefault(r => r.Kind == RowKind.Table);
        Assert.NotNull(table);
        Assert.Equal(0, table!.FirstLine);
        Assert.Equal(2, table.LastLine);
        Assert.NotNull(table.Table);
        Assert.Contains(rows, r => r.Kind == RowKind.Text && r.FirstLine == 3);   // "end" is a normal row
    }

    [Fact]
    public void Caret_inside_the_table_reveals_its_lines_raw()
    {
        var (texts, starts) = Lines("| A | B |", "| - | - |", "| 1 | 2 |", "end");
        var rows = LivePreviewLayout.Build(texts, starts, Plan(4, revealLine: 2), System.Array.Empty<WikiLink>(), null);
        Assert.DoesNotContain(rows, r => r.Kind == RowKind.Table);
        Assert.All(rows.Where(r => r.FirstLine <= 2), r => Assert.True(r.Revealed));
    }
}
