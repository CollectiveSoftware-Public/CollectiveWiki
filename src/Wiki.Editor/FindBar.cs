// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Wiki.Editor;

/// <summary>The in-note Find bar (Ctrl+F): a slim strip the host docks above the active editor. It searches
/// the attached <see cref="LivePreviewSurface"/>'s text via the pure <see cref="InNoteFinder"/> and
/// highlights the current match by selecting its range on the surface. Enter / ↓ next, Shift+Enter / ↑
/// previous, Esc closes and refocuses the editor. Code-built + delegate-free, matching the head's other
/// code-only controls; the matching logic underneath is unit-tested.</summary>
public sealed class FindBar : UserControl
{
    private readonly TextBox _box;
    private readonly TextBox _replaceBox;
    private readonly StackPanel _replaceRow;
    private readonly TextBlock _count;
    private readonly ToggleButton _caseToggle;

    private LivePreviewSurface? _surface;
    private IReadOnlyList<InNoteFinder.Match> _matches = Array.Empty<InNoteFinder.Match>();
    private int _current = -1;
    private bool _matchCase;

    public FindBar()
    {
        _box = new TextBox
        {
            PlaceholderText = "Find in note…",
            Width = 220,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(8, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(_box, "Find in note");
        _box.TextChanged += (_, _) => Refresh(fromCaret: true);
        _box.KeyDown += OnBoxKeyDown;

        _count = new TextBlock
        {
            Classes = { "muted" },
            FontSize = 11.5,
            MinWidth = 40,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };

        _caseToggle = new ToggleButton { Content = "Aa", Padding = new Thickness(6, 2), FontSize = 12 };
        AutomationProperties.SetName(_caseToggle, "Match case");
        ToolTip.SetTip(_caseToggle, "Match case");
        _caseToggle.IsCheckedChanged += (_, _) => { _matchCase = _caseToggle.IsChecked == true; Refresh(fromCaret: false); };

        var prev = MakeButton("▲", "Previous match", () => Step(forward: false));
        var next = MakeButton("▼", "Next match", () => Step(forward: true));
        var close = MakeButton("✕", "Close find", Close);

        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _box, _count, prev, next, _caseToggle, close },
        };

        // Row 2 (Ctrl+H): the replace box + Replace / All buttons; hidden for a plain find.
        _replaceBox = new TextBox
        {
            PlaceholderText = "Replace with…",
            Width = 220,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(8, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(_replaceBox, "Replace with");
        _replaceBox.KeyDown += OnReplaceBoxKeyDown;

        var replaceOne = MakeButton("Replace", "Replace the current match", ReplaceCurrent);
        var replaceAll = MakeButton("All", "Replace every match", ReplaceAllMatches);
        _replaceRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            Children = { _replaceBox, replaceOne, replaceAll },
        };

        var rows = new StackPanel { Spacing = 0, Children = { strip, _replaceRow } };

        var bar = new Border { Padding = new Thickness(6, 3), Child = rows };
        Dyn(bar, Border.BackgroundProperty, "ChromeBrush");
        Dyn(bar, Border.BorderBrushProperty, "PaneBrush");
        bar.BorderThickness = new Thickness(0, 0, 0, 1);

        HorizontalAlignment = HorizontalAlignment.Stretch;
        Content = bar;
        IsVisible = false;
    }

    /// <summary>Docks onto <paramref name="surface"/> and opens seeded with <paramref name="seed"/> (the
    /// surface's current selection); <paramref name="showReplace"/> reveals the replace row (Ctrl+H).
    /// Focuses the query box; the text change drives the first match.</summary>
    public void Attach(LivePreviewSurface? surface, string seed, bool showReplace = false)
    {
        _surface = surface;
        if (surface is null) return;
        IsVisible = true;
        _replaceRow.IsVisible = showReplace;
        _box.Text = seed;
        Refresh(fromCaret: true);
        Dispatcher.UIThread.Post(() => { _box.Focus(); _box.SelectAll(); });
    }

    public void Close()
    {
        IsVisible = false;
        _surface?.Focus();
    }

    private void OnBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: Step(forward: !e.KeyModifiers.HasFlag(KeyModifiers.Shift)); e.Handled = true; break;
            case Key.Down: Step(forward: true); e.Handled = true; break;
            case Key.Up: Step(forward: false); e.Handled = true; break;
            case Key.Escape: Close(); e.Handled = true; break;
        }
    }

    // Recompute matches for the current query. fromCaret picks the match at/after the caret (a fresh search
    // or a re-type); otherwise keep the current index in range (a match-case toggle).
    private void Refresh(bool fromCaret)
    {
        if (_surface is null) return;
        _matches = InNoteFinder.Find(_surface.CurrentText, _box.Text ?? "", _matchCase);
        if (_matches.Count == 0) { _current = -1; UpdateCount(); return; }
        _current = fromCaret
            ? InNoteFinder.Next(_matches, _surface.CaretOffset - 1, forward: true)   // include a match at the caret
            : Math.Clamp(_current, 0, _matches.Count - 1);
        SelectCurrent();
    }

    private void Step(bool forward)
    {
        if (_surface is null || _matches.Count == 0) return;
        int from = _current >= 0 ? _matches[_current].Start : _surface.CaretOffset;
        _current = InNoteFinder.Next(_matches, from, forward);
        SelectCurrent();
    }

    private void SelectCurrent()
    {
        if (_surface is null || _current < 0) { UpdateCount(); return; }
        var m = _matches[_current];
        _surface.SelectRange(m.Start, m.Length);
        UpdateCount();
    }

    private void OnReplaceBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: ReplaceCurrent(); e.Handled = true; break;
            case Key.Escape: Close(); e.Handled = true; break;
        }
    }

    // Replace the current match, then land on the next one (offsets shift, so recompute from the caret).
    private void ReplaceCurrent()
    {
        if (_surface is null || _current < 0 || _current >= _matches.Count) return;
        var m = _matches[_current];
        _surface.ReplaceRange(m.Start, m.Length, _replaceBox.Text ?? "");
        Refresh(fromCaret: true);
    }

    // Replace every match as ONE document edit (a single undo unit); reports the count in the bar.
    private void ReplaceAllMatches()
    {
        if (_surface is null) return;
        string q = _box.Text ?? "";
        string text = _surface.CurrentText;
        string result = InNoteFinder.ReplaceAll(text, q, _replaceBox.Text ?? "", _matchCase, out int n);
        if (n > 0) _surface.ReplaceRange(0, text.Length, result);
        _matches = Array.Empty<InNoteFinder.Match>();
        _current = -1;
        _count.Text = $"{n} replaced";
    }

    private void UpdateCount()
        => _count.Text = _matches.Count == 0
            ? (string.IsNullOrEmpty(_box.Text) ? "" : "0/0")
            : $"{_current + 1}/{_matches.Count}";

    private static Button MakeButton(string glyph, string name, Action onClick)
    {
        var b = new Button
        {
            Content = glyph,
            Padding = new Thickness(7, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 12,
        };
        AutomationProperties.SetName(b, name);
        ToolTip.SetTip(b, name);
        b.Click += (_, _) => onClick();
        return b;
    }

    private void Dyn(AvaloniaObject target, AvaloniaProperty property, string resourceKey)
        => target.Bind(property, this.GetResourceObservable(resourceKey));
}
