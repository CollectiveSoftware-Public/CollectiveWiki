// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Wiki.Core.Graph;

namespace Wiki.Editor;

/// <summary>A custom-drawn local link-graph: nodes are notes, lines are resolved wikilinks. Positions come
/// from the pure <see cref="GraphLayout"/> (deterministic). Clicking near a node raises the supplied open
/// callback with its note path. No AXAML — drawn like the other Wiki surfaces.</summary>
public sealed class GraphView : Control
{
    private readonly IGraphModel _model;
    private readonly IReadOnlyList<GraphLayout.NodePos> _pos;
    private readonly Dictionary<string, GraphLayout.NodePos> _byPath;
    private readonly System.Action<string> _onOpen;
    private readonly string? _center;

    private readonly IBrush _edge = new SolidColorBrush(Color.FromArgb(0x66, 0x88, 0x88, 0x88));
    private readonly IBrush _node = new SolidColorBrush(Color.Parse("#0284C7"));
    private readonly IBrush _centerNode = new SolidColorBrush(Color.Parse("#F97316"));
    private readonly IBrush _label = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
    private const double R = 6, Pad = 24;

    // Pan/zoom state (screen space). Left-drag on empty space pans; the wheel zooms about the cursor.
    private double _zoom = 1;
    private Vector _pan;
    private Point? _dragStart;
    private Vector _panStart;
    private bool _dragged;

    /// <param name="positions">Precomputed layout (so a large vault's O(n²) force layout can run off the UI
    /// thread); when null the layout is computed inline (fine for small local graphs).</param>
    public GraphView(IGraphModel model, System.Action<string> onOpen, string? center = null,
                     IReadOnlyList<GraphLayout.NodePos>? positions = null)
    {
        _model = model;
        _onOpen = onOpen;
        _center = center;
        _pos = positions ?? GraphLayout.Compute(model);
        _byPath = new Dictionary<string, GraphLayout.NodePos>(System.StringComparer.Ordinal);
        foreach (var p in _pos) _byPath[p.NotePath] = p;
        Focusable = true;
        ClipToBounds = true;
    }

    private Point Screen(GraphLayout.NodePos p, Size s)
    {
        double bx = Pad + p.X * (s.Width - 2 * Pad);
        double by = Pad + p.Y * (s.Height - 2 * Pad);
        return new Point(bx * _zoom + _pan.X, by * _zoom + _pan.Y);
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), new Rect(size));

        var pen = new Pen(_edge, 1);
        foreach (var e in _model.Edges)
        {
            if (!_byPath.TryGetValue(e.FromNote, out var a) || !_byPath.TryGetValue(e.ToNote, out var b)) continue;
            context.DrawLine(pen, Screen(a, size), Screen(b, size));
        }

        foreach (var p in _pos)
        {
            var c = Screen(p, size);
            context.DrawEllipse(p.NotePath == _center ? _centerNode : _node, null, c, R, R);
            string name = System.IO.Path.GetFileNameWithoutExtension(p.NotePath);
            var ft = new FormattedText(name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Typeface.Default, 11, _label);
            context.DrawText(ft, new Point(c.X + R + 3, c.Y - 7));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _panStart = _pan;
        _dragged = false;
        e.Pointer.Capture(this);
        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_dragStart is { } start && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var delta = e.GetPosition(this) - start;
            if (Math.Abs(delta.X) + Math.Abs(delta.Y) > 3) _dragged = true;
            if (_dragged) { _pan = _panStart + delta; InvalidateVisual(); }
        }
        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragStart is not null && !_dragged) OpenNodeAt(e.GetPosition(this));   // a click, not a pan
        _dragStart = null;
        e.Pointer.Capture(null);
        base.OnPointerReleased(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        double old = _zoom;
        _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1), 0.2, 5);
        var pt = e.GetPosition(this);   // keep the point under the cursor fixed
        _pan = new Vector(pt.X - (pt.X - _pan.X) * (_zoom / old), pt.Y - (pt.Y - _pan.Y) * (_zoom / old));
        InvalidateVisual();
        e.Handled = true;
    }

    private void OpenNodeAt(Point pt)
    {
        var size = Bounds.Size;
        string? best = null;
        double bestD = double.MaxValue;
        foreach (var p in _pos)
        {
            var c = Screen(p, size);
            double d = (c.X - pt.X) * (c.X - pt.X) + (c.Y - pt.Y) * (c.Y - pt.Y);
            if (d < bestD) { bestD = d; best = p.NotePath; }
        }
        double hit = (R + 8) * _zoom;
        if (best is not null && bestD <= hit * hit) _onOpen(best);
    }

    protected override Size MeasureOverride(Size availableSize) => availableSize;
}
