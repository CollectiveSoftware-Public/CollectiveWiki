// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Editor;

/// <summary>Column alignment from a GFM delimiter cell (<c>:--</c>, <c>--:</c>, <c>:--:</c>, <c>---</c>).</summary>
public enum TableAlign { Left, Center, Right }

/// <summary>A parsed GFM table: header cells, body rows (each padded/truncated to the header width), per-column
/// alignment, and the source line range <c>[StartLine, EndLine]</c> (inclusive).</summary>
public sealed record TableModel(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<TableAlign> Aligns,
    int StartLine,
    int EndLine);

/// <summary>Pure GFM-table detection over a note's lines. UI-free + unit-tested; the layout consumes the
/// model to draw a grid.</summary>
public static class MarkdownTable
{
    public static bool TryParse(IReadOnlyList<string> lines, int startLine, out TableModel model)
    {
        model = null!;
        if (startLine + 1 >= lines.Count) return false;

        var headers = SplitRow(lines[startLine]);
        if (headers is null || headers.Count == 0) return false;

        var aligns = ParseDelimiter(lines[startLine + 1], headers.Count);
        if (aligns is null) return false;

        var rows = new List<IReadOnlyList<string>>();
        int line = startLine + 2;
        for (; line < lines.Count; line++)
        {
            var cells = SplitRow(lines[line]);
            if (cells is null) break;
            rows.Add(Normalize(cells, headers.Count));
        }

        model = new TableModel(headers, rows, aligns, startLine, line - 1);
        return true;
    }

    // A pipe-delimited row -> trimmed cells, or null when the line has no pipe / is blank. Leading and
    // trailing pipes are optional (GFM allows both forms).
    private static List<string>? SplitRow(string line)
    {
        string t = line.Trim();
        if (t.Length == 0 || !t.Contains('|')) return null;
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|')) t = t[..^1];
        var cells = t.Split('|');
        var result = new List<string>(cells.Length);
        foreach (var c in cells) result.Add(c.Trim());
        return result;
    }

    // The `| :-- | --: |` row: every cell must be a run of '-' optionally bracketed by ':'. Returns per-column
    // alignment, or null when it is not a valid delimiter row.
    private static List<TableAlign>? ParseDelimiter(string line, int columns)
    {
        var cells = SplitRow(line);
        if (cells is null || cells.Count != columns) return null;
        var aligns = new List<TableAlign>(cells.Count);
        foreach (var raw in cells)
        {
            string c = raw.Trim();
            bool left = c.StartsWith(':');
            bool right = c.EndsWith(':');
            string dashes = c.Trim(':');
            if (dashes.Length == 0 || dashes.Any(ch => ch != '-')) return null;
            aligns.Add(left && right ? TableAlign.Center : right ? TableAlign.Right : TableAlign.Left);
        }
        return aligns;
    }

    private static IReadOnlyList<string> Normalize(List<string> cells, int columns)
    {
        if (cells.Count == columns) return cells;
        var r = new List<string>(columns);
        for (int i = 0; i < columns; i++) r.Add(i < cells.Count ? cells[i] : "");
        return r;
    }
}
