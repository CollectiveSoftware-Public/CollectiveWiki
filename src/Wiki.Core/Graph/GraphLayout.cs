// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Graph;

/// <summary>Deterministic Fruchterman–Reingold-style layout for the link-graph view. Nodes are seeded on a
/// ring (no RNG, so the result is stable + unit-testable), then relaxed by edge attraction / all-pairs
/// repulsion, and finally normalized into <c>[0.08, 0.92]²</c>. Pure — no Avalonia.</summary>
public static class GraphLayout
{
    public readonly record struct NodePos(string NotePath, double X, double Y);

    public static IReadOnlyList<NodePos> Compute(IGraphModel g, int iterations = 250)
    {
        var nodes = g.Nodes;
        int n = nodes.Count;
        if (n == 0) return System.Array.Empty<NodePos>();
        if (n == 1) return new[] { new NodePos(nodes[0].NotePath, 0.5, 0.5) };

        var idx = new Dictionary<string, int>(System.StringComparer.Ordinal);
        for (int i = 0; i < n; i++) idx[nodes[i].NotePath] = i;

        var px = new double[n];
        var py = new double[n];
        for (int i = 0; i < n; i++)
        {
            double a = 2 * System.Math.PI * i / n;   // deterministic ring seed
            px[i] = System.Math.Cos(a);
            py[i] = System.Math.Sin(a);
        }

        double k = System.Math.Sqrt(1.0 / n);        // ideal edge length in unit space
        double temp = 0.1;
        var dx = new double[n];
        var dy = new double[n];

        for (int it = 0; it < iterations; it++)
        {
            System.Array.Clear(dx, 0, n);
            System.Array.Clear(dy, 0, n);

            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    double vx = px[i] - px[j], vy = py[i] - py[j];
                    double d2 = vx * vx + vy * vy;
                    if (d2 < 1e-6) { vx = 1e-3 * ((i + j) % 2 == 0 ? 1 : -1); vy = 1e-3; d2 = vx * vx + vy * vy; }
                    double d = System.Math.Sqrt(d2);
                    double f = k * k / d;             // repulsion
                    dx[i] += vx / d * f; dy[i] += vy / d * f;
                    dx[j] -= vx / d * f; dy[j] -= vy / d * f;
                }

            foreach (var e in g.Edges)
            {
                if (!idx.TryGetValue(e.FromNote, out int a) || !idx.TryGetValue(e.ToNote, out int b)) continue;
                double vx = px[a] - px[b], vy = py[a] - py[b];
                double d = System.Math.Sqrt(vx * vx + vy * vy) + 1e-9;
                double f = d * d / k;                 // attraction
                dx[a] -= vx / d * f; dy[a] -= vy / d * f;
                dx[b] += vx / d * f; dy[b] += vy / d * f;
            }

            for (int i = 0; i < n; i++)
            {
                double dlen = System.Math.Sqrt(dx[i] * dx[i] + dy[i] * dy[i]) + 1e-9;
                double step = System.Math.Min(dlen, temp);
                px[i] += dx[i] / dlen * step;
                py[i] += dy[i] / dlen * step;
            }
            temp *= 0.99;
        }

        double minx = px.Min(), maxx = px.Max(), miny = py.Min(), maxy = py.Max();
        double sx = maxx - minx < 1e-9 ? 1 : maxx - minx;
        double sy = maxy - miny < 1e-9 ? 1 : maxy - miny;
        var res = new NodePos[n];
        for (int i = 0; i < n; i++)
            res[i] = new NodePos(nodes[i].NotePath, 0.08 + 0.84 * (px[i] - minx) / sx, 0.08 + 0.84 * (py[i] - miny) / sy);
        return res;
    }
}
