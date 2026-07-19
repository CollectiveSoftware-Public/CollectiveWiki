// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Wiki.Core.Journal;
using Wiki.Core.Search;
using Wiki.Core.Workspace;
using Wiki.Desktop.Sync;

namespace Wiki.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private VaultSession? _session;
    private IDailyNotes? _daily;
    private string? _vaultRoot;
    private readonly NavigationHistory _history = new();
    private bool _historyNavigating;

    /// <summary>Whether back/forward navigation is currently possible — mirrors this window's history so the
    /// toolbar buttons and Go-menu items enable/disable in step. Refreshed by <see cref="RaiseNavChanged"/>
    /// after every history change (a visit, a back/forward jump, or a vault switch that clears history).</summary>
    public bool CanGoBack => _history.CanGoBack;
    public bool CanGoForward => _history.CanGoForward;

    private void RaiseNavChanged()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    [ObservableProperty] private string _vaultName = "No vault open";
    [ObservableProperty] private string? _currentNote;
    [ObservableProperty] private bool _backlinksVisible;
    [ObservableProperty] private string _backlinksHeader = "Backlinks";

    /// <summary>True once a vault has been opened — drives the first-run empty state (<c>!VaultOpen</c>).
    /// <see cref="HasVault"/> stays the logic check the rest of the VM uses.</summary>
    [ObservableProperty] private bool _vaultOpen;

    /// <summary>How many notes in the vault are sync conflict copies (0 hides the status-bar counter).</summary>
    [ObservableProperty] private int _conflictCount;

    /// <summary>The first conflicted-copy note, opened when the status-bar conflict counter is clicked.</summary>
    public string? FirstConflictPath { get; private set; }

    /// <summary>The note sidebar as a nested folder tree, rebuilt on open/save/create.</summary>
    public ObservableCollection<VaultNode> Tree { get; } = new();
    public ObservableCollection<BacklinkRow> Backlinks { get; } = new();

    /// <summary>Where this note is named in prose but not linked (a section under the backlinks panel).</summary>
    public ObservableCollection<UnlinkedMentionRow> UnlinkedMentions { get; } = new();
    [ObservableProperty] private string _unlinkedHeader = "Unlinked mentions";

    /// <summary>Which left-rail view is showing (Files tree / Search / Tags).</summary>
    [ObservableProperty] private RailMode _rail = RailMode.Files;
    public bool IsFilesRail => Rail == RailMode.Files;
    public bool IsSearchRail => Rail == RailMode.Search;
    public bool IsTagsRail => Rail == RailMode.Tags;
    public bool IsOutlineRail => Rail == RailMode.Outline;
    public bool IsBookmarksRail => Rail == RailMode.Bookmarks;

    partial void OnRailChanged(RailMode value)
    {
        OnPropertyChanged(nameof(IsFilesRail));
        OnPropertyChanged(nameof(IsSearchRail));
        OnPropertyChanged(nameof(IsTagsRail));
        OnPropertyChanged(nameof(IsOutlineRail));
        OnPropertyChanged(nameof(IsBookmarksRail));
    }

    /// <summary>Vault-wide search results for the Search rail; each carries line snippets.</summary>
    public ObservableCollection<VaultSearchResult> SearchResults { get; } = new();

    /// <summary>The most recent Search-rail query (a plain term, or "#tag" from a tag click) — the head uses it
    /// to land a result click on the matched text.</summary>
    public string LastSearchQuery { get; private set; } = "";

    /// <summary>All vault tags with their note counts (flat) — kept for any flat consumers.</summary>
    public ObservableCollection<TagRow> Tags { get; } = new();

    /// <summary>The vault's tags as a nested tree (<c>#a/b/c</c> → hierarchy), for the Tags rail.</summary>
    public ObservableCollection<Wiki.Core.Models.TagTreeNode> TagTreeRoots { get; } = new();

    /// <summary>The vault's bookmarked note paths, for the Bookmarks rail.</summary>
    public ObservableCollection<string> Bookmarks { get; } = new();

    /// <summary>The active note's heading outline (rebuilt from the live editor text when the Outline rail shows).</summary>
    public ObservableCollection<OutlineRow> Outline { get; } = new();

    /// <summary>The split-pane group (Left + optional Right). Two-pane cap.</summary>
    public PaneGroupViewModel Panes { get; } = new();

    /// <summary>The active pane's tab strip — all existing tab operations target the focused pane.</summary>
    public OpenTabsViewModel Tabs => Panes.ActivePane;

    /// <summary>Raised when the active tab (of the active pane) changes; the view swaps the hosted control.</summary>
    public event Action<EditorTab?>? ActiveTabChanged;

    public MainViewModel()
    {
        Panes.ActiveChanged += (_, _) => OnActiveTabChangedInternal();
    }

    public bool HasVault => _session is not null;
    public string? VaultRoot => _vaultRoot;

    // Opening the backlinks panel enables the (O(vault)) unlinked-mentions scan — recompute so they appear.
    partial void OnBacklinksVisibleChanged(bool value)
    {
        if (value) RefreshBacklinks();
    }

    private void OnActiveTabChangedInternal()
    {
        var active = Tabs.Active;
        CurrentNote = active?.Kind == TabKind.Note ? active.NotePath : active?.Title;
        // Every active-note change (in-place navigation, tab open, tab switch) is a history visit —
        // except the jump a GoBack/GoForward itself performs.
        if (!_historyNavigating && active is { Kind: TabKind.Note, NotePath: { } visited }) _history.Visit(visited);
        ActiveTabChanged?.Invoke(active);
        if (active?.Kind == TabKind.Note) RefreshBacklinks();
        else { Backlinks.Clear(); BacklinksHeader = "Backlinks"; }
        RaiseNavChanged();   // a visit (or a jump landing here) may have changed what's reachable
    }

    /// <summary>Opens a vault. The heavy index rebuild runs on a background thread (a large vault takes
    /// seconds to parse), so the UI thread never freezes; the await resumes on the UI thread to publish
    /// the note list and open the first note.</summary>
    public async Task OpenVaultAsync(string root)
    {
        VaultName = "Opening…";
        var result = await Task.Run(() => VaultWorkspace.Open(root));

        _session = result.Session;
        _daily = result.DailyNotes;
        _vaultRoot = root;
        VaultName = System.IO.Path.GetFileName(root.TrimEnd('/', '\\'));

        // Switching vaults: drop the previous vault's open tabs (and, via Active→null, its current-note /
        // backlinks state) so they don't linger over the new vault. The view guards unsaved work before
        // calling here (see MainWindow.OpenVaultPathAsync).
        Panes.CloseAllPanes();
        _history.Clear();
        RaiseNavChanged();   // fresh vault → no history yet

        BuildTree(result.Notes);
        RefreshTags();
        RefreshBookmarks();
        SearchResults.Clear();
        Rail = RailMode.Files;
        if (result.Notes.Count > 0) OpenNote(result.Notes[0]);
        VaultOpen = true;
    }

    /// <summary>The note-path list the current <see cref="Tree"/> was built from — used to skip a rebuild
    /// (which would collapse folder expansion) when a save/navigation left the note set unchanged.</summary>
    private IReadOnlyList<string>? _builtNotes;

    private void RefreshNotes()
    {
        if (_session is null) { Tree.Clear(); _builtNotes = null; return; }
        var current = _session.Notes();
        if (VaultTreeBuilder.SameNotes(_builtNotes, current)) return;   // unchanged — keep the tree + expansion
        BuildTree(current);
    }

    private void BuildTree(IReadOnlyList<string> notePaths)
    {
        Tree.Clear();
        var folders = _session?.Folders() ?? Array.Empty<string>();
        foreach (var node in VaultTreeBuilder.Build(notePaths, folders)) Tree.Add(node);
        _builtNotes = notePaths;
        ConflictCount = ConflictCopy.Count(notePaths);
        FirstConflictPath = notePaths.FirstOrDefault(ConflictCopy.IsConflictNote);
    }

    /// <summary>Resolves an embed target to an absolute image path on disk (vault root + resolved
    /// relative path), or null. The editor calls this to load <c>![[image.png]]</c> embeds.</summary>
    public string? ResolveImageAbsolute(string target)
    {
        var session = _session;
        var root = _vaultRoot;
        if (session is null || root is null) return null;
        var rel = session.ResolveAssetPath(target);
        return rel is null ? null : System.IO.Path.Combine(root, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
    }

    /// <summary>The raw text of a note (for the view to populate a tab's editor surface).</summary>
    public string ReadNote(string notePath) => _session?.Read(notePath) ?? "";

    /// <summary>Every note's '/'-relative path paired with its text — the source for a whole-vault HTML export.</summary>
    public IReadOnlyList<(string Path, string Text)> AllNotesWithText()
        => _session is { } s ? s.Notes().Select(p => (p, s.Read(p))).ToList() : Array.Empty<(string, string)>();

    /// <summary>All '/'-relative folders in the vault (for the Move-to folder picker).</summary>
    public IReadOnlyList<string> FolderPaths() => _session?.Folders() ?? Array.Empty<string>();

    /// <summary>Applies persisted vault preferences (attachments / templates folders) to the open session.
    /// Called by the head after a vault opens and whenever Settings is saved.</summary>
    public void ApplyPreferences(string attachmentsFolder, string templatesFolder)
    {
        if (_session is null) return;
        if (!string.IsNullOrWhiteSpace(attachmentsFolder)) _session.AttachmentsFolder = attachmentsFolder.Trim();
        if (!string.IsNullOrWhiteSpace(templatesFolder)) _session.TemplatesFolder = templatesFolder.Trim();
    }

    /// <summary>Saves a pasted image into the vault and returns the embed target (bare file name) to insert,
    /// or null when no vault is open. The editor calls this on an image paste.</summary>
    public string? SaveClipboardImage(byte[] data, string extension) => _session?.SaveAsset(data, extension);

    /// <summary>Ranked note-title candidates for the editor's [[ autocomplete popup (empty without a vault).</summary>
    public IReadOnlyList<string> LinkCandidates(string query)
        => _session is { } s ? Wiki.Editor.LinkCompletion.Candidates(s.Notes(), query) : Array.Empty<string>();

    /// <summary>Ranked tag candidates for the editor's # autocomplete popup (empty without a vault).</summary>
    public IReadOnlyList<string> TagCandidates(string query)
        => _session is { } s ? Wiki.Editor.TagCompletion.Candidates(s.AllTags(), query) : Array.Empty<string>();

    /// <summary>Ranked quick-switcher results for the Ctrl+O overlay — title matches first, then content
    /// matches from the vault index (a note is read only to build a content-hit snippet). Empty when no
    /// vault is open, so the overlay just shows nothing.</summary>
    public IReadOnlyList<SwitcherHit> QuerySwitcher(string query)
    {
        var session = _session;
        if (session is null) return [];
        return QuickSwitcher.Query(session.Notes(), query, q => session.Search(q), session.Read);
    }

    /// <summary>Runs a vault-wide search and fills <see cref="SearchResults"/> (cleared on an empty query).</summary>
    public void RunSearch(string query)
    {
        LastSearchQuery = query?.Trim() ?? "";
        SearchResults.Clear();
        var session = _session;
        if (session is null || string.IsNullOrWhiteSpace(query)) return;
        foreach (var r in session.SearchWithSnippets(query)) SearchResults.Add(r);
    }

    /// <summary>Renames a tag across the whole vault (rewriting every note that carries it) and refreshes the
    /// tag list. Returns the changed note paths so the head can reload any open tabs whose file changed.</summary>
    public IReadOnlyList<string> RenameTagAt(string oldTag, string newTag)
    {
        if (_session is not { } s) return Array.Empty<string>();
        var changed = s.RenameTag(oldTag, newTag);
        RefreshTags();
        return changed;
    }

    /// <summary>Shows the notes carrying <paramref name="tag"/> in the Search rail (a tag-line snippet each).</summary>
    public void ShowTag(string tag)
    {
        LastSearchQuery = "#" + tag;
        SearchResults.Clear();
        var session = _session;
        if (session is not null)
            foreach (var path in session.NotesWithTag(tag))
                SearchResults.Add(new VaultSearchResult(path, NoteTitle(path),
                    SnippetBuilder.Build(session.Read(path), "#" + tag, 1)));
        Rail = RailMode.Search;
    }

    /// <summary>Rebuilds the tag list (on vault open, and after a save that may have changed a note's tags).</summary>
    public void RefreshTags()
    {
        Tags.Clear();
        TagTreeRoots.Clear();
        var session = _session;
        if (session is null) return;
        var flat = new List<(string, int)>();
        foreach (var tag in session.AllTags())
        {
            int count = session.NotesWithTag(tag).Count;
            Tags.Add(new TagRow(tag, count));
            flat.Add((tag, count));
        }
        foreach (var node in Wiki.Core.Models.TagTree.Build(flat)) TagTreeRoots.Add(node);
    }

    /// <summary>Shows every note under a tag path (a Tags-rail node click) in the Search rail — prefix-aware,
    /// so <c>#area</c> lists notes tagged <c>#area</c> and any <c>#area/*</c> child.</summary>
    public void ShowTagPrefix(string fullPath)
    {
        LastSearchQuery = "#" + fullPath;
        SearchResults.Clear();
        if (_session is not { } s) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in s.AllTags())
            if (string.Equals(tag, fullPath, StringComparison.OrdinalIgnoreCase)
                || tag.StartsWith(fullPath + "/", StringComparison.OrdinalIgnoreCase))
                foreach (var path in s.NotesWithTag(tag))
                    if (seen.Add(path))
                        SearchResults.Add(new VaultSearchResult(path, NoteTitle(path),
                            SnippetBuilder.Build(s.Read(path), "#" + tag, 1)));
        Rail = RailMode.Search;
    }

    /// <summary>Rebuilds the Bookmarks rail from the session's persisted bookmark list.</summary>
    public void RefreshBookmarks()
    {
        Bookmarks.Clear();
        if (_session is { } s) foreach (var p in s.Bookmarks.Paths) Bookmarks.Add(p);
    }

    /// <summary>Whether <paramref name="notePath"/> is bookmarked (drives the context-menu label).</summary>
    public bool IsBookmarked(string notePath) => _session?.Bookmarks.IsBookmarked(notePath) ?? false;

    /// <summary>Toggles a note's bookmark and refreshes the rail.</summary>
    public void ToggleBookmark(string notePath)
    {
        if (_session is not { } s) return;
        s.Bookmarks.Toggle(notePath);
        RefreshBookmarks();
    }

    /// <summary>Toggles the active note's bookmark (Ctrl+D / View menu / palette).</summary>
    public void ToggleBookmarkActive()
    {
        if (CurrentNote is { } n) ToggleBookmark(n);
    }

    /// <summary>The display title (file name without <c>.md</c>) for a bookmark row.</summary>
    public static string BookmarkTitle(string notePath) => System.IO.Path.GetFileNameWithoutExtension(notePath);

    /// <summary>Replaces the outline with headings parsed from <paramref name="noteText"/> (the live editor
    /// text). Called by the head when the Outline rail is shown or the active note changes.</summary>
    public void SetOutline(string noteText)
    {
        Outline.Clear();
        foreach (var h in Wiki.Core.Outline.OutlineBuilder.Build(noteText))
            Outline.Add(new OutlineRow(h.Title, h.Level, h.Offset));
    }

    private static string NoteTitle(string notePath) => System.IO.Path.GetFileNameWithoutExtension(notePath);

    /// <summary>Opens (or focuses) a tab for a note.</summary>
    public void OpenNote(string notePath)
    {
        if (_session is null) return;
        Tabs.OpenNote(notePath, NoteTitle(notePath), activate: true);
    }

    /// <summary>Navigates the active tab to a note in place (a left-click in the tree / a plain wikilink
    /// click — reuses the current tab instead of opening a new one).</summary>
    public void NavigateActiveTab(string notePath)
    {
        if (_session is null) return;
        Tabs.NavigateActive(notePath, NoteTitle(notePath));
    }

    /// <summary>Opens (or focuses) a full-size image tab.</summary>
    public void OpenImageTab(string absPath, string title)
        => Tabs.OpenImage(absPath, title, activate: true);

    /// <summary>Navigates the active tab to the previous/next note in this window's history. A history
    /// jump is not itself recorded; entries whose note no longer exists are skipped over.</summary>
    public void GoBack() => GoHistory(() => _history.GoBack());
    public void GoForward() => GoHistory(() => _history.GoForward());

    private void GoHistory(Func<string?> step)
    {
        if (_session is not { } session) return;
        var notes = session.Notes();
        string? path;
        while ((path = step()) is not null && !notes.Contains(path)) { }
        if (path is null) return;
        _historyNavigating = true;
        try { NavigateActiveTab(path); }
        finally { _historyNavigating = false; }
        RaiseNavChanged();   // the cursor moved — refresh both directions (the navigate above was a no-record jump)
    }

    /// <summary>Opens the active note's local link-graph (its neighborhood, depth 2) in a graph tab. Falls back
    /// to the whole-vault graph when no note is active.</summary>
    public void OpenLocalGraph()
    {
        if (_session is null) return;
        var full = _session.BuildGraph();
        string? center = CurrentNote;
        var model = center is null ? full : full.Neighborhood(center, 2);
        string title = center is null ? "Graph" : "Graph: " + NoteTitle(center);
        Tabs.OpenGraph("graph:" + (center ?? "*"), model, title, center);
    }

    /// <summary>Opens the whole-vault link graph in a tab. The graph build + force layout are O(vault), so
    /// they run off the UI thread (a large vault would otherwise freeze the window); the tab opens on the UI
    /// thread with the precomputed positions.</summary>
    public async Task OpenGlobalGraphAsync()
    {
        if (_session is not { } session) return;
        var (model, positions) = await Task.Run(() =>
        {
            var g = session.BuildGraph();
            return (g, Wiki.Core.Graph.GraphLayout.Compute(g));
        });
        Tabs.OpenGraph("graph:*all", model, "Vault Graph", center: null, positions: positions);
    }

    public void CloseTab(EditorTab tab) => Tabs.Close(tab);

    /// <summary>Closes a tab through the guarded path (the save-before-close prompt wired on
    /// <see cref="OpenTabsViewModel.ClosingAsync"/>); returns false when the close was vetoed.</summary>
    public Task<bool> CloseTabAsync(EditorTab tab) => Tabs.CloseAsync(tab);

    /// <summary>Saves the active note tab's text.</summary>
    public void SaveActive(string text)
    {
        var active = Tabs.Active;
        if (_session is null || active is null || active.Kind != TabKind.Note || active.NotePath is null) return;
        _session.Save(active.NotePath, text);
        active.IsDirty = false;
        RefreshNotes();
        RefreshBacklinks();
        RefreshTags();
    }

    /// <summary>Saves an arbitrary note tab's text (generalizes <see cref="SaveActive"/> to a tab that
    /// isn't the active one — the guarded-close prompt uses this to save a dirty background tab).</summary>
    public void SaveTab(EditorTab tab, string text)
    {
        if (_session is null || tab.Kind != TabKind.Note || tab.NotePath is null) return;
        _session.Save(tab.NotePath, text);
        tab.IsDirty = false;
        RefreshNotes();
        RefreshBacklinks();
        RefreshTags();
    }

    /// <summary>Renames a note (rewriting inbound links + index via the session) and keeps open tabs
    /// consistent: an already-open tab on the old path is retargeted to the new note in place when it was
    /// active (so the user stays on it), or closed when it was a background tab (its file just moved —
    /// nothing unsaved to lose). Returns the new path (== the old path for a no-op rename).</summary>
    public string RenameNoteAt(string notePath, string newTitle)
    {
        if (_session is null) return notePath;
        var newPath = _session.RenameNote(notePath, newTitle);
        if (newPath == notePath) return notePath;

        var open = Tabs.Tabs.FirstOrDefault(t => t.Kind == TabKind.Note && t.NotePath == notePath);
        bool wasActive = open is not null && ReferenceEquals(Tabs.Active, open);
        RefreshNotes();
        if (wasActive) NavigateActiveTab(newPath);   // retarget the active slot in place
        else if (open is not null) Tabs.Close(open); // a stale background tab
        return newPath;
    }

    /// <summary>Deletes a note (session drops it from disk + index) and closes any tab open on it.</summary>
    public void DeleteNoteAt(string notePath)
    {
        if (_session is null) return;
        if (Tabs.Tabs.FirstOrDefault(t => t.Kind == TabKind.Note && t.NotePath == notePath) is { } open)
            Tabs.Close(open);
        _session.DeleteNote(notePath);
        RefreshNotes();
    }

    public void NewNote()
    {
        if (_session is null) return;
        var path = _session.CreateNote("Untitled");
        RefreshNotes();
        OpenNote(path);
    }

    /// <summary>Creates a named note in <paramref name="folder"/> ("" = root) and opens it.</summary>
    public void CreateNoteIn(string folder, string title)
    {
        if (_session is null) return;
        var path = _session.CreateNote(title, folder);
        BuildTree(_session.Notes());
        RefreshTags();
        OpenNote(path);
    }

    /// <summary>Creates an (initially empty) folder and shows it in the tree.</summary>
    public void CreateFolderIn(string parentFolder, string name)
    {
        if (_session is null) return;
        _session.CreateFolder(parentFolder, name);
        BuildTree(_session.Notes());
    }

    /// <summary>Moves a note into <paramref name="folder"/> ("" = root), keeping its open tab on the note.</summary>
    public void MoveNoteToFolder(string notePath, string folder)
    {
        if (_session is null) return;
        var newPath = _session.MoveNote(notePath, folder);
        if (newPath == notePath) return;
        RetargetMovedTab(notePath, newPath);
        BuildTree(_session.Notes());
        RefreshBacklinks();
    }

    /// <summary>Renames a folder in place (notes moved, links preserved); closes any tabs it held.</summary>
    public void RenameFolderAt(string folderPath, string newName)
    {
        if (_session is null) return;
        _session.RenameFolder(folderPath, newName);
        CloseTabsUnder(folderPath);
        BuildTree(_session.Notes());
        RefreshTags();
    }

    /// <summary>Moves a folder under <paramref name="newParent"/> ("" = root); closes any tabs it held.</summary>
    public void MoveFolderToParent(string folderPath, string newParent)
    {
        if (_session is null) return;
        _session.MoveFolder(folderPath, newParent);
        CloseTabsUnder(folderPath);
        BuildTree(_session.Notes());
        RefreshTags();
    }

    /// <summary>Deletes a folder and every note under it; closes any tabs it held.</summary>
    public void DeleteFolderAt(string folderPath)
    {
        if (_session is null) return;
        CloseTabsUnder(folderPath);
        _session.DeleteFolder(folderPath);
        BuildTree(_session.Notes());
        RefreshTags();
    }

    // Retarget an open tab on a moved note: keep the active slot on it (in place), else close the stale
    // background tab (its file just moved — nothing unsaved to lose there).
    private void RetargetMovedTab(string oldPath, string newPath)
    {
        var open = Tabs.Tabs.FirstOrDefault(t => t.Kind == TabKind.Note && t.NotePath == oldPath);
        if (open is null) return;
        if (ReferenceEquals(Tabs.Active, open)) NavigateActiveTab(newPath);
        else Tabs.Close(open);
    }

    // Close every open note tab whose file lives under a folder that just moved/was deleted (their paths
    // are now stale). Structural op initiated by the user, so background edits are not preserved.
    private void CloseTabsUnder(string folderPath)
    {
        string prefix = folderPath.TrimEnd('/') + "/";
        foreach (var t in Tabs.Tabs
                     .Where(t => t.Kind == TabKind.Note && t.NotePath is { } p
                                 && p.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            Tabs.Close(t);
    }

    public void OpenDailyNote()
    {
        if (_session is null || _daily is null) return;
        var path = _daily.GetOrCreateToday();
        RefreshNotes();
        OpenNote(path);
    }

    /// <summary>Template names available for Insert Template / New from Template.</summary>
    public IReadOnlyList<string> TemplateNames() => _session?.ListTemplates() ?? Array.Empty<string>();

    /// <summary>Renders template <paramref name="name"/> for the active note's title, for caret insertion.</summary>
    public string RenderTemplateForActive(string name)
    {
        if (_session is null) return "";
        string title = CurrentNote is { } p ? NoteTitle(p) : "Untitled";
        return _session.RenderTemplate(name, title);
    }

    /// <summary>Creates a note from template <paramref name="name"/> and opens it.</summary>
    public void NewFromTemplate(string name, string title, string folder)
    {
        if (_session is null) return;
        var path = _session.CreateFromTemplate(name, title, folder);
        BuildTree(_session.Notes());
        RefreshTags();
        OpenNote(path);
    }

    /// <summary>Resolves (or creates) a wikilink target and opens it in a <em>new</em> tab
    /// (Ctrl/middle-click a link); returns its path.</summary>
    public string OpenLinkTarget(string target)
    {
        if (_session is null) return target;
        var path = _session.ResolveOrCreateTarget(target);
        RefreshNotes();
        OpenNote(path);
        return path;
    }

    /// <summary>Resolves (or creates) a wikilink target and navigates the active tab to it in place
    /// (a plain left-click on a link — reuses the current tab); returns its path.</summary>
    public string NavigateLinkTarget(string target)
    {
        if (_session is null) return target;
        var path = _session.ResolveOrCreateTarget(target);
        RefreshNotes();
        Tabs.NavigateActive(path, NoteTitle(path));
        return path;
    }

    // Computing backlinks resolves every link in the vault (seconds on a large vault), so it runs on a
    // background thread; the pane then fills in. The await resumes on the UI thread (captured context),
    // and a stale result is dropped if the user has since navigated to another note.
    private async void RefreshBacklinks()
    {
        Backlinks.Clear();
        UnlinkedMentions.Clear();
        BacklinksHeader = "Backlinks";
        UnlinkedHeader = "Unlinked mentions";
        var session = _session;
        var note = CurrentNote;
        if (session is null || note is null) return;
        bool wantMentions = BacklinksVisible;   // the O(vault) text scan only when the panel is open
        try
        {
            var (backlinks, mentions) = await Task.Run(() =>
            {
                var bl = session.Backlinks(note);
                var um = new List<Wiki.Core.Indexing.UnlinkedMention>();
                if (wantMentions)
                {
                    string title = NoteTitle(note);
                    var aliases = ParseAliases(session.Read(note));
                    var others = session.Notes().Where(p => p != note).Select(p => (p, session.Read(p))).ToList();
                    um = Wiki.Core.Indexing.UnlinkedMentions.Find(title, aliases, others).ToList();
                }
                return (bl, um);
            });
            if (note != CurrentNote) return;   // navigated away while computing
            Backlinks.Clear();
            foreach (var b in backlinks) Backlinks.Add(BacklinkRow.From(b));
            BacklinksHeader = Backlinks.Count > 0 ? $"Backlinks ({Backlinks.Count})" : "Backlinks";
            UnlinkedMentions.Clear();
            foreach (var m in mentions) UnlinkedMentions.Add(UnlinkedMentionRow.From(m));
            UnlinkedHeader = mentions.Count > 0 ? $"Unlinked mentions ({mentions.Count})" : "Unlinked mentions";
        }
        catch
        {
            // backlinks/mentions are non-critical; never let a refresh failure crash the app
        }
    }

    // Reads a simple "aliases:" front-matter value (a flow list [a, b] or a single scalar) for mention matching.
    private static IReadOnlyList<string> ParseAliases(string noteText)
    {
        if (!noteText.StartsWith("---", StringComparison.Ordinal)) return Array.Empty<string>();
        int end = noteText.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return Array.Empty<string>();
        foreach (var line in noteText[..end].Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("aliases:", StringComparison.OrdinalIgnoreCase))
            {
                string v = t["aliases:".Length..].Trim();
                if (v.StartsWith('[') && v.EndsWith(']'))
                    return v[1..^1].Split(',').Select(x => x.Trim().Trim('"', '\'')).Where(x => x.Length > 0).ToList();
                return v.Length > 0 ? new[] { v.Trim('"', '\'') } : Array.Empty<string>();
            }
        }
        return Array.Empty<string>();
    }

    /// <summary>Links one unlinked mention (rewrites its source note on disk) and recomputes the panel.
    /// Returns the changed source note path so the head can reload an open tab on it.</summary>
    public string? LinkMention(Wiki.Core.Indexing.UnlinkedMention m)
    {
        if (_session is not { } s || CurrentNote is not { } note) return null;
        s.Save(m.SourceNotePath, Wiki.Core.Indexing.MentionLinker.LinkOne(s.Read(m.SourceNotePath), m, NoteTitle(note)));
        RefreshBacklinks();
        return m.SourceNotePath;
    }

    /// <summary>Links every unlinked mention in one source note at once. Returns the changed source path.</summary>
    public string? LinkAllMentions(string sourceNotePath)
    {
        if (_session is not { } s || CurrentNote is not { } note) return null;
        var forSource = UnlinkedMentions.Where(r => r.SourceNotePath == sourceNotePath).Select(r => r.Mention).ToList();
        if (forSource.Count == 0) return null;
        s.Save(sourceNotePath, Wiki.Core.Indexing.MentionLinker.LinkAll(s.Read(sourceNotePath), forSource, NoteTitle(note)));
        RefreshBacklinks();
        return sourceNotePath;
    }

    /// <summary>Returns the first CLI argument that is an existing directory (a vault to open), or null.
    /// Pure — the caller opens it via <see cref="OpenVaultAsync"/> once the window is shown.</summary>
    public string? ResolveStartupVault(string[] args)
        => args.FirstOrDefault(a => System.IO.Directory.Exists(a));
}

/// <summary>Which view the left sidebar rail is showing.</summary>
public enum RailMode { Files, Search, Tags, Outline, Bookmarks }

/// <summary>One row in the Tags rail: a tag and how many notes carry it.</summary>
public sealed record TagRow(string Tag, int Count);

/// <summary>One row in the "Unlinked mentions" panel section: the source note, a snippet, and the underlying
/// mention (carried so the Link action can rewrite exactly that occurrence).</summary>
public sealed record UnlinkedMentionRow(string Title, string Snippet, string SourceNotePath,
    Wiki.Core.Indexing.UnlinkedMention Mention)
{
    public static UnlinkedMentionRow From(Wiki.Core.Indexing.UnlinkedMention m)
        => new(System.IO.Path.GetFileNameWithoutExtension(m.SourceNotePath), m.Snippet, m.SourceNotePath, m);
}

/// <summary>One row in the Outline rail: a heading, its level (for indentation), and its source offset.</summary>
public sealed record OutlineRow(string Title, int Level, int Offset)
{
    /// <summary>Left indent (px) for the heading level (1 → 0, 2 → 12, …).</summary>
    public double Indent => (Level - 1) * 12;
}
