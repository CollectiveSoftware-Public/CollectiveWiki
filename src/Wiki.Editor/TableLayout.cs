// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Editor;

/// <summary>Pure column sizing for a rendered table: each column is as wide as its widest cell (header or
/// body) plus padding, then the set is scaled down proportionally if it would exceed the available width.
/// <paramref name="measureText"/> is injected so the math is unit-testable without a text engine.</summary>
public static class TableLayout
{
    public const double CellPadX = 12;

    public static double[] Columns(TableModel model, System.Func<string, double> measureText, double maxTotalWidth)
    {
        int cols = model.Headers.Count;
        var w = new double[cols];
        for (int c = 0; c < cols; c++) w[c] = measureText(model.Headers[c]) + CellPadX * 2;
        foreach (var row in model.Rows)
            for (int c = 0; c < cols && c < row.Count; c++)
                w[c] = System.Math.Max(w[c], measureText(row[c]) + CellPadX * 2);

        double total = 0;
        for (int c = 0; c < cols; c++) total += w[c];
        if (total > maxTotalWidth && total > 0)
        {
            double scale = maxTotalWidth / total;
            for (int c = 0; c < cols; c++) w[c] *= scale;
        }
        return w;
    }
}
