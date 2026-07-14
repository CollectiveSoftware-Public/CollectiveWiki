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

namespace Wiki.Desktop.Views;

/// <summary>The Ctrl+P command palette: a centered overlay listing app commands, filtered as you type
/// (<see cref="CommandRegistry.Filter"/>). Enter / click runs the selected command; Esc or losing focus
/// closes it. Mirrors <see cref="QuickSwitcherOverlay"/>; the ranking beneath is unit-tested.</summary>
public sealed class CommandPaletteOverlay : UserControl
{
    private readonly TextBox _box;
    private readonly ListBox _list;

    /// <summary>Supplies the filtered commands for the current query text (the head wires this to
    /// <see cref="CommandRegistry.Filter"/> over its command list).</summary>
    public Func<string, IReadOnlyList<CommandRegistry.AppCommand>>? QuerySource { get; set; }

    /// <summary>Raised when the user picks a command (Enter / click).</summary>
    public event Action<CommandRegistry.AppCommand>? Committed;

    /// <summary>Raised on Esc or when the overlay loses focus — the head hides it and refocuses the editor.</summary>
    public event Action? Dismissed;

    public CommandPaletteOverlay()
    {
        _box = new TextBox
        {
            PlaceholderText = "Type a command…",
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
            MaxHeight = 360,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(4),
            ItemTemplate = new FuncDataTemplate<CommandRegistry.AppCommand>((c, _) => BuildRow(c), supportsRecycling: true),
        };
        _list.PointerReleased += OnListPointerReleased;

        var divider = new Border { Height = 1 };
        Dyn(divider, Border.BackgroundProperty, "Collective.Border");

        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_box, Dock.Top);
        DockPanel.SetDock(divider, Dock.Top);
        body.Children.Add(_box);
        body.Children.Add(divider);
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

    /// <summary>Clears the box, shows the card, populates it, and focuses the query field.</summary>
    public void Open()
    {
        _box.Text = "";
        Requery();
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
            case Key.Enter: CommitSelected(); e.Handled = true; break;
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

    private void CommitSelected()
    {
        if (_list.SelectedItem is CommandRegistry.AppCommand cmd) Committed?.Invoke(cmd);
    }

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && _list.SelectedItem is CommandRegistry.AppCommand cmd)
        {
            Committed?.Invoke(cmd);
            e.Handled = true;
        }
    }

    private void OnBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual v
            && v.FindAncestorOfType<CommandPaletteOverlay>() == this)
            return;
        if (IsVisible) Dismissed?.Invoke();
    }

    private static Control BuildRow(CommandRegistry.AppCommand cmd)
    {
        var label = new TextBlock
        {
            Text = cmd.Label,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var gesture = new TextBlock
        {
            Text = cmd.Gesture ?? "",
            Classes = { "muted" },
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 0, 0, 0),
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(gesture, 1);
        grid.Children.Add(label);
        grid.Children.Add(gesture);
        return grid;
    }

    private void Dyn(AvaloniaObject target, AvaloniaProperty property, string resourceKey)
        => target.Bind(property, this.GetResourceObservable(resourceKey));
}
