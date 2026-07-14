// SPDX-License-Identifier: GPL-3.0-or-later
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Wiki.Desktop.ViewModels;

/// <summary>A capped two-pane split: a permanent <see cref="Left"/> tab strip and an optional
/// <see cref="Right"/> one. <see cref="Active"/> is the active tab of the active pane; <see cref="ActiveChanged"/>
/// fires whenever that changes (either pane's active tab, or the active side flipping). The head routes all
/// active-note flows (backlinks, outline, save, …) through <see cref="Active"/>.</summary>
public partial class PaneGroupViewModel : ObservableObject
{
    public enum Side { Left, Right }

    public OpenTabsViewModel Left { get; } = new();
    [ObservableProperty] private OpenTabsViewModel? _right;
    [ObservableProperty] private Side _activeSide = Side.Left;

    public OpenTabsViewModel ActivePane => ActiveSide == Side.Right && Right is { } r ? r : Left;
    public EditorTab? Active => ActivePane.Active;
    public bool IsSplit => Right is not null;

    public event EventHandler? ActiveChanged;

    private Func<EditorTab, Task<bool>>? _closingAsync;

    /// <summary>The save-before-close veto hook; applied to both panes (and any newly created right pane).</summary>
    public Func<EditorTab, Task<bool>>? ClosingAsync
    {
        get => _closingAsync;
        set
        {
            _closingAsync = value;
            Left.ClosingAsync = value;
            if (Right is { } r) r.ClosingAsync = value;
        }
    }

    public PaneGroupViewModel() => Left.PropertyChanged += OnPaneChanged;

    /// <summary>Creates (or refocuses) the right pane and makes it active. The caller opens the target note.</summary>
    public OpenTabsViewModel SplitRight()
    {
        if (Right is null)
        {
            Right = new OpenTabsViewModel { ClosingAsync = _closingAsync };
            Right.PropertyChanged += OnPaneChanged;
        }
        ActiveSide = Side.Right;   // OnActiveSideChanged raises ActiveChanged
        return Right;
    }

    /// <summary>Collapses back to a single pane (drops the right pane's tabs).</summary>
    public void CloseRight()
    {
        if (Right is not { } r) return;
        r.PropertyChanged -= OnPaneChanged;
        r.CloseAll();
        Right = null;
        ActiveSide = Side.Left;
        Raise();
    }

    /// <summary>Sets which pane is active (a click/focus in it). No-op for the right pane when there isn't one.</summary>
    public void Focus(Side side)
    {
        if (side == Side.Right && Right is null) return;
        if (ActiveSide != side) ActiveSide = side; else Raise();
    }

    /// <summary>Closes every tab in both panes and collapses to a single (empty) left pane (vault switch).</summary>
    public void CloseAllPanes()
    {
        Left.CloseAll();
        if (Right is { } r) { r.PropertyChanged -= OnPaneChanged; r.CloseAll(); Right = null; }
        ActiveSide = Side.Left;
    }

    private void OnPaneChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OpenTabsViewModel.Active)) Raise();
    }

    partial void OnActiveSideChanged(Side value) => Raise();
    private void Raise() { OnPropertyChanged(nameof(Active)); ActiveChanged?.Invoke(this, EventArgs.Empty); }
}
