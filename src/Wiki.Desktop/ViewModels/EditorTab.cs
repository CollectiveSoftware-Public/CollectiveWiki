// SPDX-License-Identifier: GPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;

namespace Wiki.Desktop.ViewModels;

public enum TabKind { Note, Image, Graph }

/// <summary>One open document tab: a note (live-preview editor), an image (full-size viewer), or a link
/// graph. <see cref="Key"/> is the dedup identity (one tab per note path / image path / graph centre).</summary>
public partial class EditorTab : ObservableObject
{
    public TabKind Kind { get; init; }
    public string Key { get; init; } = "";
    public string? NotePath { get; init; }
    public string? ImagePath { get; init; }

    /// <summary>The graph model shown by a <see cref="TabKind.Graph"/> tab (null otherwise).</summary>
    public Wiki.Core.Graph.IGraphModel? Graph { get; init; }

    /// <summary>The centre note of a local-graph tab (highlighted), or null for the full-vault graph.</summary>
    public string? GraphCenter { get; init; }

    /// <summary>Precomputed force-layout positions for a graph tab (built off-thread for the whole-vault
    /// graph so it doesn't freeze on open); null lets the view lay out inline.</summary>
    public IReadOnlyList<Wiki.Core.Graph.GraphLayout.NodePos>? GraphPositions { get; init; }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isActive;
}
