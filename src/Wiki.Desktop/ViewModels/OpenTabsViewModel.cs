// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.ObjectModel;
using Collective.Platform.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Wiki.Desktop.ViewModels;

/// <summary>The open-tabs strip: a list of <see cref="EditorTab"/>s plus the active one. One tab per
/// distinct note/image (re-opening focuses the existing tab); closing the active tab activates a
/// neighbour. Pure view-model logic — unit-tested without any UI. The tab mechanics are delegated to
/// the shared <see cref="TabList{T}"/>; this view-model keeps Wiki's public surface (note/image key
/// scheme, image tabs never reused by navigation) and re-raises <c>Active</c> as an
/// <see cref="ObservableObject"/> property change so the view/MainViewModel react as before.</summary>
public partial class OpenTabsViewModel : ObservableObject
{
    private readonly TabList<EditorTab> _list = new(t => t.Key);

    public OpenTabsViewModel()
    {
        // Mirror the old [ObservableProperty] Active: update each tab's IsActive flag and raise the
        // "Active" PropertyChanged (MainViewModel listens for exactly nameof(Active)).
        _list.ActiveChanged += (_, _) =>
        {
            foreach (var t in _list.Tabs) t.IsActive = ReferenceEquals(t, _list.Active);
            OnPropertyChanged(nameof(Active));
        };
    }

    public ObservableCollection<EditorTab> Tabs => _list.Tabs;

    public EditorTab? Active
    {
        get => _list.Active;
        set => _list.Active = value;
    }

    public EditorTab OpenNote(string notePath, string title, bool activate)
    {
        string key = "note:" + notePath;
        var tab = _list.OpenOrActivate(
            key, () => new EditorTab { Kind = TabKind.Note, Key = key, NotePath = notePath, Title = title }, activate);
        if (Active is null) Active = tab;   // the very first tab becomes active even without activate
        return tab;
    }

    public EditorTab OpenImage(string absPath, string title, bool activate)
    {
        string key = "img:" + absPath;
        var tab = _list.OpenOrActivate(
            key, () => new EditorTab { Kind = TabKind.Image, Key = key, ImagePath = absPath, Title = title }, activate);
        if (Active is null) Active = tab;   // the very first tab becomes active even without activate
        return tab;
    }

    public EditorTab OpenGraph(string key, Wiki.Core.Graph.IGraphModel graph, string title, string? center,
        IReadOnlyList<Wiki.Core.Graph.GraphLayout.NodePos>? positions = null)
    {
        var tab = _list.OpenOrActivate(
            key, () => new EditorTab { Kind = TabKind.Graph, Key = key, Title = title, Graph = graph, GraphCenter = center, GraphPositions = positions }, activate: true);
        if (Active is null) Active = tab;
        return tab;
    }

    /// <summary>Navigates the active tab to a note <em>in place</em> (reusing the same slot:
    /// a left-click in the file tree replaces what the current tab shows rather than opening a new one).
    /// If the note is already open in another tab it focuses that (no duplicate); if there is no active
    /// note tab to reuse (nothing active, or the active tab is an image) it opens a new one.</summary>
    public EditorTab NavigateActive(string notePath, string title)
    {
        string key = "note:" + notePath;
        var existing = Tabs.FirstOrDefault(t => t.Key == key);
        if (existing is not null) { Active = existing; return existing; }

        var active = Active;
        if (active is null || active.Kind != TabKind.Note)
            return OpenNote(notePath, title, activate: true);   // nothing note-shaped to reuse → new tab

        // Reuse the active note tab's slot (drops the old one's view).
        return _list.NavigateActive(
            key, () => new EditorTab { Kind = TabKind.Note, Key = key, NotePath = notePath, Title = title });
    }

    public void Close(EditorTab tab) => _list.Close(tab);

    /// <summary>Closes every open tab (synchronous, no save prompt — the vault-switch flow guards unsaved
    /// work before calling this). Leaves <see cref="Active"/> null and the collection empty; used when
    /// switching vaults so the old vault's tabs don't linger.</summary>
    public void CloseAll()
    {
        foreach (var tab in _list.Tabs.ToArray())
            _list.Close(tab);
    }

    /// <summary>Veto hook consulted by <see cref="CloseAsync"/> (the save-before-close prompt); proxies the
    /// shared <see cref="TabList{T}"/> hook. Return <c>false</c> from it to cancel a close. The synchronous
    /// <see cref="Close"/> deliberately ignores this — callers that must not prompt keep using it.</summary>
    public Func<EditorTab, Task<bool>>? ClosingAsync
    {
        get => _list.ClosingAsync;
        set => _list.ClosingAsync = value;
    }

    /// <summary>Closes a tab through the veto hook (prompts to save when dirty). Returns <c>false</c> when
    /// the close was vetoed; on success removes the tab and activates the neighbour (next-else-previous).</summary>
    public Task<bool> CloseAsync(EditorTab tab) => _list.CloseAsync(tab);

    // ---- scoped bulk closes (tab right-click menu) ----
    // The logic lives once in the shared TabList<T> (guarded per-tab, vetoed save aborts the rest);
    // these just expose it on Wiki's tab facade. CloseAllGuardedAsync is distinct from the silent
    // CloseAll() used for the vault-switch teardown.

    public Task CloseOthersAsync(EditorTab keep) => _list.CloseOthersAsync(keep);
    public Task CloseToLeftAsync(EditorTab anchor) => _list.CloseToLeftAsync(anchor);
    public Task CloseToRightAsync(EditorTab anchor) => _list.CloseToRightAsync(anchor);
    public Task CloseAllGuardedAsync() => _list.CloseAllAsync();
}
