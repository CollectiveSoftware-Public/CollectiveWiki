// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wiki.Core.Search;

namespace Wiki.Desktop.Views;

/// <summary>The Ctrl+O quick switcher: a centered overlay card that queries the vault index (via
/// <see cref="QuerySource"/>) and lets the user jump to a note — Enter opens in place, Ctrl+Enter opens
/// a new tab, Esc (or losing focus) closes it. Code-built + delegate-driven (no ICommand/DI), the same
/// pattern as the head's other overlays; the ranking beneath it is unit-tested in Wiki.Core
/// (<see cref="QuickSwitcher"/>), so this control only wires text → query → keyboard selection.</summary>
public sealed class QuickSwitcherOverlay : UserControl
{
    private readonly TextBox _box;
    private readonly ListBox _list;

    /// <summary>Supplies ranked results for the current query text (the head wires this to
    /// <c>MainViewModel.QuerySwitcher</c> over the vault index). Null → no results.</summary>
    public Func<string, IReadOnlyList<SwitcherHit>>? QuerySource { get; set; }

    /// <summary>Raised when the user picks a hit; the bool is true for "open in a new tab" (Ctrl+Enter),
    /// false for "open in place" (Enter / click).</summary>
    public event Action<SwitcherHit, bool>? Committed;

    /// <summary>Raised on Esc or when the overlay loses focus — the head hides it and refocuses the editor.</summary>
    public event Action? Dismissed;

    /// <summary>The query box watermark; the head sets "Open a vault first" when no vault is open.</summary>
    public string PlaceholderText
    {
        get => _box.PlaceholderText ?? "";
        set => _box.PlaceholderText = value;
    }

    public QuickSwitcherOverlay()
    {
        _box = new TextBox
        {
            PlaceholderText = "Search notes…",
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(12, 9),
            FontSize = 14,
        };
        _box.TextChanged += (_, _) => Requery();
        _box.KeyDown += OnBoxKeyDown;
        _box.LostFocus += OnBoxLostFocus;

        _list = new ListBox
        {
            MaxHeight = 340,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(4),
            ItemTemplate = new FuncDataTemplate<SwitcherHit>((hit, _) => BuildRow(hit), supportsRecycling: true),
        };
        _list.PointerReleased += OnListPointerReleased;

        var divider = new Border { Height = 1 };
        Dyn(divider, Border.BackgroundProperty, "Collective.Border");

        var footer = new Border { Padding = new Thickness(12, 6) };
        Dyn(footer, Border.BackgroundProperty, "ChromeBrush");
        footer.Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Children =
            {
                Hint("Enter  Open"),
                Hint("Ctrl+Enter  New tab"),
                Hint("↑↓  Select"),
                Hint("Esc  Close"),
            },
        };

        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_box, Dock.Top);
        DockPanel.SetDock(divider, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        body.Children.Add(_box);
        body.Children.Add(divider);
        body.Children.Add(footer);
        body.Children.Add(_list);

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            BoxShadow = BoxShadows.Parse("0 12 32 0 #33000000"),
            Child = body,
        };
        Dyn(card, Border.BackgroundProperty, "EditorAreaBrush");
        Dyn(card, Border.BorderBrushProperty, "Collective.Border");

        Content = card;
    }

    /// <summary>Clears the box, shows the card, and focuses the query field (posted so it lands after the
    /// panel becomes visible and is laid out). Text change then drives <see cref="Requery"/>.</summary>
    public void Open()
    {
        _box.Text = "";
        _list.ItemsSource = null;
        IsVisible = true;
        Dispatcher.UIThread.Post(() => _box.Focus());
    }

    private void Requery()
    {
        var results = QuerySource?.Invoke(_box.Text ?? "") ?? [];
        _list.ItemsSource = results;
        _list.SelectedIndex = results.Count > 0 ? 0 : -1;
    }

    private void OnBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: Move(1); e.Handled = true; break;
            case Key.Up: Move(-1); e.Handled = true; break;
            case Key.Enter: CommitSelected(e.KeyModifiers.HasFlag(KeyModifiers.Control)); e.Handled = true; break;
            case Key.Escape: Dismissed?.Invoke(); e.Handled = true; break;
        }
    }

    private void Move(int delta)
    {
        int count = _list.ItemCount;
        if (count == 0) return;
        int i = _list.SelectedIndex;
        i = i < 0 ? (delta > 0 ? 0 : count - 1) : Math.Clamp(i + delta, 0, count - 1);
        _list.SelectedIndex = i;
        _list.ScrollIntoView(i);
    }

    private void CommitSelected(bool newTab)
    {
        if (_list.SelectedItem is SwitcherHit hit) Committed?.Invoke(hit, newTab);
    }

    // A left-click on a result opens it (Ctrl+click → new tab). The ListBox has already set SelectedItem
    // on the press, so the released item is the selected one.
    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && _list.SelectedItem is SwitcherHit hit)
        {
            Committed?.Invoke(hit, e.KeyModifiers.HasFlag(KeyModifiers.Control));
            e.Handled = true;
        }
    }

    // Losing focus dismisses — unless focus merely moved to the results list (a click), which is still
    // inside the overlay; dismissing then would cancel the click before it commits.
    private void OnBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual v
            && v.FindAncestorOfType<QuickSwitcherOverlay>() == this)
            return;
        if (IsVisible) Dismissed?.Invoke();
    }

    private static Control BuildRow(SwitcherHit hit)
    {
        var title = new TextBlock
        {
            Text = hit.Title,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var folder = new TextBlock
        {
            Text = hit.Folder,
            Classes = { "muted" },
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 0, 0, 0),
        };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(folder, 1);
        top.Children.Add(title);
        top.Children.Add(folder);

        if (hit.Kind == SwitcherHitKind.Content && hit.Snippet is { } snippet)
        {
            var snip = new TextBlock
            {
                Text = snippet,
                Classes = { "muted" },
                FontSize = 11.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 1, 0, 0),
            };
            return new StackPanel { Children = { top, snip } };
        }
        return top;
    }

    private static Control Hint(string text) =>
        new TextBlock { Text = text, Classes = { "muted" }, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };

    private void Dyn(AvaloniaObject target, AvaloniaProperty property, string resourceKey)
        => target.Bind(property, this.GetResourceObservable(resourceKey));
}
