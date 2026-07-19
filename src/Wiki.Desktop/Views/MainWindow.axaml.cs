// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Collective.Platform;
using Wiki.Core.Search;
using Collective.Platform.Abstractions;
using Collective.Platform.Controls;
using Collective.Platform.Secrets;
using Wiki.Core.Workspace;
using Wiki.Desktop.Sync;
using Wiki.Desktop.Update;
using Wiki.Desktop.ViewModels;
using Wiki.Editor;
using Wiki.Sync;
using Wiki.Update;

namespace Wiki.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly ISettingsStore _store;
    private readonly AppSettings _settings;
    private UpdateCoordinator? _updates;
    private static readonly HttpClient _updateHttp = new() { Timeout = TimeSpan.FromMinutes(10) };
    private string _themeMode = "System";
    private string? _startupVault;
    // Debounced autosave: each edit restarts this one-shot timer; it fires once the note has been idle
    // for AutosaveDelayMs. Save also fires on window focus loss. The manual Ctrl+S / close-prompt path
    // stays as a belt-and-suspenders safety net.
    private DispatcherTimer? _autosaveTimer;

    // One content control per open tab (a live-preview surface or an image viewer), shown in TabHost.
    private readonly Dictionary<EditorTab, Control> _tabControls = new();
    private LivePreviewSurface? _activeSurface;
    // Bridges the shared CollectiveDocs formatting toolbar to the active markdown surface (persists across
    // tab switches; rebound in ShowActiveTab).
    private readonly LivePreviewFormattingAdapter _fmtAdapter = new();
    private string _lastSwitcherQuery = "";   // last Ctrl+O query, so a content-hit commit can jump to the match
    private VaultNode? _contextNode;   // the tree note last right-clicked (for the context-menu commands)
    private bool _revealingInTree;     // guards OnNoteSelected while we set the selection ourselves
    private SyncViewModel? _sync;       // P2P sync for the open vault (null until a vault opens)
    // Folder paths the user has expanded, remembered across tree rebuilds so a create/rename/delete
    // doesn't collapse the whole tree (a save/navigation that leaves the note set unchanged skips the
    // rebuild entirely — see MainViewModel.RefreshNotes).
    private readonly HashSet<string> _expandedFolders = new(StringComparer.Ordinal);
    // Shared MRU of recently-opened vault folders (empty-state list + File ▸ Open Recent), persisted
    // through the same ISettingsStore under its own "recentVaults" blob.
    private readonly RecentItemsStore _recentVaults;
    // Owns all top-level windows; routes "open vault" to a new/existing window (one window per vault).
    private readonly WikiWindowManager _manager;

    // Binds an outline row's numeric indent to a left-only Thickness.
    public static readonly Avalonia.Data.Converters.IValueConverter LeftMargin =
        new Avalonia.Data.Converters.FuncValueConverter<double, Thickness>(x => new Thickness(x, 0, 0, 0));

    // Displays a '/'-relative note path as its title (file name without .md) — for the Bookmarks rail.
    public static readonly Avalonia.Data.Converters.IValueConverter NoteTitleOf =
        new Avalonia.Data.Converters.FuncValueConverter<string?, string>(
            p => p is null ? "" : System.IO.Path.GetFileNameWithoutExtension(p));

    // Collection-count → IsVisible (and its inverse, for empty-state hints).
    public static readonly Avalonia.Data.Converters.IValueConverter CountToVisible =
        new Avalonia.Data.Converters.FuncValueConverter<int, bool>(n => n > 0);
    public static readonly Avalonia.Data.Converters.IValueConverter CountToHidden =
        new Avalonia.Data.Converters.FuncValueConverter<int, bool>(n => n == 0);

    public MainWindow() { _store = null!; _settings = new(); _manager = null!; _recentVaults = new(_store, "recentVaults", 8); InitializeComponent(); }   // designer

    public MainWindow(MainViewModel vm, ISettingsStore store, AppSettings settings, WikiWindowManager manager, string? startupVault = null)
    {
        _store = store;
        _settings = settings;
        _manager = manager;
        _recentVaults = new RecentItemsStore(store, "recentVaults", 8);
        _themeMode = settings.ThemeMode;
        _startupVault = startupVault;
        vm.BacklinksVisible = settings.BacklinksVisible;
        DataContext = vm;
        InitializeComponent();

        FormatToolbar.Surface = _fmtAdapter;   // gates the shared toolbar to markdown-expressible clusters

        vm.ActiveTabChanged += ShowActiveTab;
        vm.Tabs.Tabs.CollectionChanged += OnTabsCollectionChanged;
        vm.PropertyChanged += OnVmPropertyChanged;   // keep the status-bar conflict counter in sync

        // The tab strip is the shared DocumentTabStrip: it renders the tab collection, left-click
        // activates (sets Active, same as the old Border.tab click), and both the ✕ button and a
        // middle-click raise TabCloseRequested. The active highlight follows Tabs.Active via
        // ShowActiveTab setting TabStrip.SelectedItem.
        // Left pane strip (always present). Clicking a tab activates it AND focuses the left pane.
        WireTabStrip(TabStrip, PaneGroupViewModel.Side.Left);
        TabStrip.ItemsSource = vm.Panes.Left.Tabs;
        // Right pane strip (bound lazily when the pane is created; only visible when split).
        WireTabStrip(TabStripRight, PaneGroupViewModel.Side.Right);

        // Closing a dirty note tab prompts Save / Don't save / Cancel via the shared dialog. A tab with no
        // created control cannot be dirty (only the surface's TextChanged sets IsDirty), so a TryGetValue
        // miss here means "nothing to save" and is safe. Applied to both panes via the pane group.
        vm.Panes.ClosingAsync = async tab =>
        {
            if (tab is not { IsDirty: true, Kind: TabKind.Note }) return true;
            var r = await SaveChangesDialog.ShowAsync(this, $"Save changes to “{tab.Title}”?");
            if (r == SaveChangesResult.Cancel) return false;
            if (r == SaveChangesResult.Save && _tabControls.TryGetValue(tab, out var c) && c is LivePreviewSurface s)
                Vm!.SaveTab(tab, s.GetText());
            return true;
        };

        // Focus follows a click into a pane (tunnelling, so the editor surface doesn't swallow it first).
        PaneLeftBorder.AddHandler(PointerPressedEvent,
            (_, _) => Vm?.Panes.Focus(PaneGroupViewModel.Side.Left), RoutingStrategies.Tunnel);
        PaneRightBorder.AddHandler(PointerPressedEvent,
            (_, _) => Vm?.Panes.Focus(PaneGroupViewModel.Side.Right), RoutingStrategies.Tunnel);

        // Ctrl+O quick switcher: query the vault index, then open the picked note in place (Enter/click)
        // or a new tab (Ctrl+Enter). Both a commit and a dismiss hide the overlay and refocus the editor.
        Switcher.QuerySource = q => { _lastSwitcherQuery = q; return Vm?.QuerySwitcher(q) ?? []; };
        Switcher.Committed += (hit, newTab) =>
        {
            if (newTab) Vm?.OpenNote(hit.NotePath);
            else NavigateTo(hit.NotePath);
            // A content hit means the match is inside the note body — land on it, not the top.
            if (hit.Kind == SwitcherHitKind.Content && _lastSwitcherQuery is { Length: > 0 })
                _activeSurface?.SelectFirstMatch(_lastSwitcherQuery);
            CloseSwitcher();
        };
        Switcher.Dismissed += CloseSwitcher;

        // Ctrl+P command palette: filter the head's command list, run the picked command on commit.
        Palette.QuerySource = q => CommandRegistry.Filter(BuildCommands(), q);
        Palette.Committed += cmd => { ClosePalette(); cmd.Run(); };
        Palette.Dismissed += ClosePalette;

        // Empty-state recent-vaults rows: folder leaf name over the muted full path; a press opens it.
        RecentVaultsList.ItemTemplate = new FuncDataTemplate<RecentVaultRow>((_, _) =>
        {
            var name = new TextBlock();
            name.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(RecentVaultRow.Name)));
            var path = new TextBlock { Classes = { "muted" }, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
            path.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(RecentVaultRow.Path)));
            var row = new Border
            {
                Padding = new Thickness(6, 4), CornerRadius = new CornerRadius(4), Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel { Children = { name, path } },
            };
            row.PointerPressed += OnRecentVaultPressed;
            return row;
        });

        ApplyThemeMode(_themeMode, persist: false);
        ThemeController.OnActualThemeChanged(this, variant => ApplyEditorTheme(variant));
        // Window geometry restore-before-show + persist-on-close is now handled by WindowStateService
        // (wired in App.axaml.cs), backed by its own "window" settings blob.

        // Track folder expand/collapse (the routed events bubble from each TreeViewItem to the TreeView) so
        // the state survives a rebuild; realized containers re-apply it in OnTreeItemAttached.
        NotesTree.AddHandler(TreeViewItem.ExpandedEvent, OnTreeItemExpanded);
        NotesTree.AddHandler(TreeViewItem.CollapsedEvent, OnTreeItemCollapsed);
        NotesTree.AddHandler(DragDrop.DragOverEvent, OnTreeDragOver);
        NotesTree.AddHandler(DragDrop.DropEvent, OnTreeDrop);

        KeyDown += OnKeyDown;
        // Mouse Back/Forward buttons navigate the note history (tunnel: fires before any child control).
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Closing += OnWindowClosing;
        Opened += OnOpened;
        // Persist the active note when the window loses focus (guarded by AutosaveEnabled).
        Deactivated += (_, _) => { if (_settings is { AutosaveEnabled: true }) SaveActiveSurface(onlyIfDirty: true); };
    }

    // Restart the idle autosave timer after an edit (no-op when autosave is disabled).
    private void ScheduleAutosave()
    {
        if (_settings is not { AutosaveEnabled: true }) return;
        _autosaveTimer ??= CreateAutosaveTimer();
        _autosaveTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(300, _settings.AutosaveDelayMs));
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }

    private DispatcherTimer CreateAutosaveTimer()
    {
        var t = new DispatcherTimer();
        t.Tick += (_, _) => { t.Stop(); SaveActiveSurface(onlyIfDirty: true); };
        return t;
    }

    // ---- Back / Forward (Go menu, Alt+arrows, mouse X-buttons) ----

    private void OnGoBack(object? sender, RoutedEventArgs e) => GoBack();
    private void OnGoForward(object? sender, RoutedEventArgs e) => GoForward();

    // Mirrors NavigateTo: persist the outgoing tab's edits before the in-place history jump.
    private void GoBack() { SaveActiveSurface(onlyIfDirty: true); Vm?.GoBack(); }
    private void GoForward() { SaveActiveSurface(onlyIfDirty: true); Vm?.GoForward(); }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsXButton1Pressed) { GoBack(); e.Handled = true; }
        else if (props.IsXButton2Pressed) { GoForward(); e.Handled = true; }
    }

    // ---- note-tree expansion memory (Task 2) ----

    private void OnTreeItemExpanded(object? sender, RoutedEventArgs e)
    {
        if ((e.Source as TreeViewItem)?.DataContext is VaultNode { FolderPath: { } fp }) _expandedFolders.Add(fp);
    }

    private void OnTreeItemCollapsed(object? sender, RoutedEventArgs e)
    {
        if ((e.Source as TreeViewItem)?.DataContext is VaultNode { FolderPath: { } fp }) _expandedFolders.Remove(fp);
    }

    // As each folder row's container realizes (initial layout, scroll, or after a rebuild), re-apply its
    // remembered expanded state — works at any depth because containers realize their children on expand.
    private void OnTreeItemAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control { DataContext: VaultNode { FolderPath: { } fp } } c && _expandedFolders.Contains(fp)
            && c.FindAncestorOfType<TreeViewItem>() is { } item)
            item.IsExpanded = true;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>This window's open vault root, or null when it has no vault yet — used by the window
    /// manager to route "open vault" (focus this window vs. open a new one).</summary>
    public string? VaultRoot => Vm?.VaultRoot;

    // Open the startup vault (CLI arg or restored) once the window is visible, on a background thread,
    // so a large vault never freezes startup. Runs once — always in this window (nothing to preserve yet).
    private async void OnOpened(object? sender, EventArgs e)
    {
        await RefreshRecentVaultsAsync();   // populate the empty-state list even before any vault opens
        if (_startupVault is { } v)
        {
            _startupVault = null;
            await OpenVaultInThisWindowAsync(v);
        }
        await MaybeCheckForUpdatesAtStartupAsync();
    }

    // Lazily builds the sync host for the currently-open vault, doing the I/O-heavy part — first-time device
    // identity generation, at-rest sealing, and the .cwiki/sync sidecar writes — OFF the UI thread. Returns the
    // existing host if already built, or null when no vault is open. Callers that need sync (Share/Join) await
    // this instead of assuming a host was stood up at open time. The device key lives in the OS-appropriate
    // secret store (DPAPI on Windows, an owner-only file store elsewhere — see SecretStores.CreateDefault).
    private async Task<SyncViewModel?> EnsureSyncAsync()
    {
        if (_sync is not null) return _sync;
        if (Vm?.VaultRoot is not { } root) return null;

        var service = await Task.Run(() =>
        {
            var appData = new DesktopFileSystem("CollectiveWiki").AppDataDirectory;
            ISecretStore secrets = SecretStores.CreateDefault(System.IO.Path.Combine(appData, "secrets"));
            return WikiSyncHostFactory.ForVault(root, secrets);
        });
        var sync = new SyncViewModel(service, dispatch: a => Dispatcher.UIThread.Post(a));
        sync.PropertyChanged += (_, _) => UpdateSyncStatusText();
        _sync = sync;
        UpdateSyncStatusText();
        return _sync;
    }

    // Called when a vault opens: tear down the previous vault's sync host, then stand sync up again ONLY if the
    // user actually has sync enabled — so opening a vault never creates a device identity or .cwiki/sync sidecar
    // for someone who doesn't sync (that first-time crypto + disk I/O used to run here on the UI thread and could
    // stall a cold open). When enabled, the host is built off the UI thread and auto-serves if it has a roster.
    private async Task ResetSyncForOpenVaultAsync()
    {
        _sync?.Dispose();
        _sync = null;
        UpdateSyncStatusText();
        if (!_settings.SyncEnabled) return;

        var sync = await EnsureSyncAsync();
        if (sync is { HasRoster: true, IsSharing: false })
        {
            // A second window sharing another vault would try to bind the same sync/pairing port; degrade
            // to a non-serving state rather than crashing (concurrent multi-vault sync is a follow-up).
            try { sync.StartServing(_settings.PairingPort, _settings.SyncPort, _settings.InternetSyncEnabled); }
            catch (SocketException) { /* port already served by another window — leave this vault un-shared */ }
        }
        UpdateSyncStatusText();
    }

    // ---- File ----

    private async void OnOpenVault(object? sender, RoutedEventArgs e)
    {
        var path = await FolderPickerDialog.ShowAsync(this, _settings.LastVaultPath);
        if (!string.IsNullOrEmpty(path)) _manager.OpenVault(path, this);   // new window unless this one is empty
    }

    // Opens a vault folder IN THIS window and records it in the recent-vaults MRU — used for the empty
    // window (first run) and startup restore. The manager sends every other "open vault" here or to a new
    // window. Public so the manager can call it.
    public async Task OpenVaultInThisWindowAsync(string path)
    {
        if (Vm is not { } vm) return;
        // Guard unsaved work before OpenVaultAsync clears the old vault's tabs (the swap happens while
        // _session still points at the current vault, so a Save writes to the right place).
        if (!await PromptSaveDirtyNoteTabsAsync()) return;
        _settings.LastVaultPath = path;
        SaveSettings();
        await vm.OpenVaultAsync(path);
        vm.ApplyPreferences(_settings.AttachmentsFolder, _settings.TemplatesFolder);
        await ResetSyncForOpenVaultAsync();
        await _recentVaults.AddAsync(path);
        await RefreshRecentVaultsAsync();
    }

    private sealed record RecentVaultRow(string Name, string Path);

    private void OnRecentVaultPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: RecentVaultRow row }) _manager.OpenVault(row.Path, this);
    }

    // Fills the empty-state list + rebuilds File ▸ Open Recent from the MRU, dropping entries whose folder
    // no longer exists (the MRU logic itself is Platform-tested; this is composition only).
    private async Task RefreshRecentVaultsAsync()
    {
        var rows = new List<RecentVaultRow>();
        foreach (var p in await _recentVaults.GetAsync())
        {
            if (!System.IO.Directory.Exists(p)) { await _recentVaults.RemoveAsync(p); continue; }
            rows.Add(new RecentVaultRow(System.IO.Path.GetFileName(p.TrimEnd('/', '\\')), p));
        }
        RecentVaultsList.ItemsSource = rows;

        OpenRecentMenu.Items.Clear();
        foreach (var row in rows)
        {
            var item = new MenuItem { Header = row.Path };
            item.Click += (_, _) => _manager.OpenVault(row.Path, this);
            OpenRecentMenu.Items.Add(item);
        }
        OpenRecentMenu.IsEnabled = rows.Count > 0;
    }

    private void OnNewNote(object? sender, RoutedEventArgs e) => Vm?.NewNote();
    private void OnDailyNote(object? sender, RoutedEventArgs e) => Vm?.OpenDailyNote();
    private void OnInsertTemplate(object? sender, RoutedEventArgs e) => _ = InsertTemplateAsync();
    private void OnNewFromTemplate(object? sender, RoutedEventArgs e) => _ = NewFromTemplateAsync();

    // ---- templates ----

    private async Task InsertTemplateAsync()
    {
        if (Vm is not { } vm || _activeSurface is null) return;
        var name = await PickTemplateAsync(vm, "Insert template");
        if (name is null) return;
        _activeSurface.InsertAtCaret(vm.RenderTemplateForActive(name));
    }

    private async Task NewFromTemplateAsync()
    {
        if (Vm is not { } vm) return;
        var name = await PickTemplateAsync(vm, "New note from template");
        if (name is null) return;
        var title = await PromptTextAsync("New note", "Note name:", "Untitled");
        if (string.IsNullOrWhiteSpace(title)) return;

        const string rootLabel = "(vault root)";
        var choices = new List<string> { rootLabel };
        choices.AddRange(vm.FolderPaths());
        var pickedFolder = await ChooseAsync("Create in folder", choices);
        if (pickedFolder is null) return;                 // cancelled
        string folder = pickedFolder == rootLabel ? "" : pickedFolder;

        vm.NewFromTemplate(name, title, folder);
        _ = _sync?.NotifyLocalChangeAsync();
    }

    // Pick a template name; tells the user when the templates folder is empty (a common first-run state).
    private async Task<string?> PickTemplateAsync(MainViewModel vm, string title)
    {
        var names = vm.TemplateNames();
        if (names.Count == 0)
        {
            await ConfirmDialog.ShowAsync(this, "No templates",
                "No templates found. Add .md files to the templates folder (Settings ▸ Templates folder).",
                confirmText: "OK", destructive: false);
            return null;
        }
        return await ChooseAsync(title, names);
    }

    // A small single-choice list dialog (OK / double-click commits). Returns the picked item or null.
    private async Task<string?> ChooseAsync(string title, IReadOnlyList<string> items)
    {
        var list = new ListBox { ItemsSource = items, SelectedIndex = 0, Height = 220 };
        string? result = null;
        var ok = new Button { Content = "OK", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var dlg = new Window
        {
            Title = title, Width = 360, Height = 320, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16), Spacing = 10,
                Children =
                {
                    list,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { ok, cancel },
                    },
                },
            },
        };
        ok.Click += (_, _) => { result = list.SelectedItem as string; dlg.Close(); };
        cancel.Click += (_, _) => dlg.Close();
        list.DoubleTapped += (_, _) => { result = list.SelectedItem as string; dlg.Close(); };
        await dlg.ShowDialog(this);
        return result;
    }
    private void OnSave(object? sender, RoutedEventArgs e) => SaveActiveSurface();

    // ---- HTML export (File ▸ Export) ----

    // Resolve an embedded asset to a data: URI (null if not found) so HtmlExporter stays IO-free.
    private string? AssetDataUri(string target)
    {
        if (Vm?.ResolveImageAbsolute(target) is not { } abs || !System.IO.File.Exists(abs)) return null;
        string ext = System.IO.Path.GetExtension(abs).TrimStart('.').ToLowerInvariant();
        string mime = ext switch
        {
            "png" => "image/png", "jpg" or "jpeg" => "image/jpeg", "gif" => "image/gif",
            "webp" => "image/webp", "svg" => "image/svg+xml", _ => "application/octet-stream",
        };
        return $"data:{mime};base64," + System.Convert.ToBase64String(System.IO.File.ReadAllBytes(abs));
    }

    private async void OnExportNote(object? sender, RoutedEventArgs e)
    {
        if (_activeSurface is not { } surface || Vm?.Tabs.Active is not { Kind: TabKind.Note, NotePath: { } path }) return;
        string title = System.IO.Path.GetFileNameWithoutExtension(path);
        string html = Wiki.Core.Export.HtmlExporter.RenderNote(surface.CurrentText, title, AssetDataUri, _ => "#");
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = title + ".html",
            FileTypeChoices = new[] { new FilePickerFileType("HTML") { Patterns = new[] { "*.html" } } },
        });
        if (file?.TryGetLocalPath() is { } p) await System.IO.File.WriteAllTextAsync(p, html);
    }

    private async void OnCopyHtml(object? sender, RoutedEventArgs e)
    {
        if (_activeSurface is not { } surface) return;
        string html = Wiki.Core.Export.HtmlExporter.RenderNote(surface.CurrentText, "note", AssetDataUri, _ => "#");
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(html);
    }

    private async void OnExportVault(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Export vault to folder", AllowMultiple = false });
        if (folders is not { Count: > 0 } || folders[0].TryGetLocalPath() is not { } dir) return;
        foreach (var (rel, html) in Wiki.Core.Export.HtmlExporter.RenderVault(vm.AllNotesWithText(), AssetDataUri))
        {
            string outPath = System.IO.Path.Combine(dir, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outPath)!);
            await System.IO.File.WriteAllTextAsync(outPath, html);
        }
    }
    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    // ---- active-note file commands (File menu + palette; the tree context menu covers any note) ----

    private string? ActiveNotePath => Vm?.Tabs.Active is { Kind: TabKind.Note, NotePath: { } p } ? p : null;

    private void OnRenameActive(object? sender, RoutedEventArgs e) { if (ActiveNotePath is { } p) _ = RenameNoteAsync(p); }
    private void OnMoveActive(object? sender, RoutedEventArgs e) { if (ActiveNotePath is { } p) _ = MoveNoteAsync(p); }
    private void OnDeleteActive(object? sender, RoutedEventArgs e) { if (ActiveNotePath is { } p) _ = DeleteNoteAsync(p); }
    private void OnCloseTab(object? sender, RoutedEventArgs e) { if (Vm?.Tabs.Active is { } t) _ = Vm.CloseTabAsync(t); }

    // Go menu — the visible entry points for the keyboard shortcuts. Search / Command Palette / Find are
    // fleshed out in later tasks (they are no-ops until then, but the menu structure lands here).
    private void OnQuickSwitch(object? sender, RoutedEventArgs e) => OpenQuickSwitcher();
    private void OnFocusSearch(object? sender, RoutedEventArgs e) => FocusSearchRail();
    private void OnCommandPalette(object? sender, RoutedEventArgs e) => Palette.Open();
    private void OnFindInNote(object? sender, RoutedEventArgs e) => _activeSurface?.OpenFind();
    private void OnReplaceInNote(object? sender, RoutedEventArgs e) => _activeSurface?.OpenReplace();

    private void ClosePalette()
    {
        Palette.IsVisible = false;
        _activeSurface?.Focus();
    }

    // The command-palette command set. Rebuilt per open so gestures/actions reflect current state; each
    // Run() reuses an existing handler/VM method (the palette is just another entry point).
    private List<CommandRegistry.AppCommand> BuildCommands()
    {
        var list = new List<CommandRegistry.AppCommand>();
        void Add(string label, string? gesture, Action run) => list.Add(new CommandRegistry.AppCommand(label, label, gesture, run));

        Add("New Note", "Ctrl+N", () => Vm?.NewNote());
        Add("Daily Note", null, () => Vm?.OpenDailyNote());
        Add("New Note from Template…", null, () => { _ = NewFromTemplateAsync(); });
        Add("Insert Template…", null, () => { _ = InsertTemplateAsync(); });
        Add("Save", "Ctrl+S", () => SaveActiveSurface());
        Add("Open Vault…", null, () => OnOpenVault(null, new RoutedEventArgs()));
        Add("Back", "Alt+Left", GoBack);
        Add("Forward", "Alt+Right", GoForward);
        Add("Quick Switch…", "Ctrl+O", OpenQuickSwitcher);
        Add("Search in Vault…", "Ctrl+Shift+F", FocusSearchRail);
        Add("Find in Note…", "Ctrl+F", () => _activeSurface?.OpenFind());
        Add("Replace in Note…", "Ctrl+H", () => _activeSurface?.OpenReplace());
        Add("Rename Note…", null, () => { if (ActiveNotePath is { } p) _ = RenameNoteAsync(p); });
        Add("Move Note…", null, () => { if (ActiveNotePath is { } p) _ = MoveNoteAsync(p); });
        Add("Delete Note", null, () => { if (ActiveNotePath is { } p) _ = DeleteNoteAsync(p); });
        Add("Close Tab", "Ctrl+W", () => { if (Vm?.Tabs.Active is { } t) _ = Vm.CloseTabAsync(t); });
        Add("Toggle Reading View", "Ctrl+E", () => OnToggleReadMode(null, new RoutedEventArgs()));
        Add("Toggle Focus Mode", null, () => OnToggleFocusMode(null, new RoutedEventArgs()));
        Add("Zoom In", "Ctrl+=", () => _activeSurface?.ChangeZoom(1));
        Add("Zoom Out", "Ctrl+-", () => _activeSurface?.ChangeZoom(-1));
        Add("Reset Zoom", "Ctrl+0", () => _activeSurface?.ResetZoom());
        Add("Local Graph", "Ctrl+G", () => Vm?.OpenLocalGraph());
        Add("Vault Graph", "Ctrl+Shift+G", () => _ = Vm?.OpenGlobalGraphAsync());
        Add("Split Right", "Ctrl+\\", SplitRight);
        Add("Close Pane", "Ctrl+Shift+\\", CloseRightPane);
        Add("Copy Wikilink", null, () => { if (ActiveNotePath is { } p) CopyWikilink(p); });
        Add("Reveal in Explorer", null, () => { if (ActiveNotePath is { } p) RevealInExplorer(p); });
        Add("Toggle Backlinks", null, () => { if (Vm is { } v) v.BacklinksVisible = !v.BacklinksVisible; });
        Add("Edit Properties…", "Ctrl+;", () => _ = EditActiveProperties());
        Add("Export Note as HTML…", null, () => OnExportNote(null, new RoutedEventArgs()));
        Add("Export Vault as HTML…", null, () => OnExportVault(null, new RoutedEventArgs()));
        Add("Copy as HTML", null, () => OnCopyHtml(null, new RoutedEventArgs()));
        Add("Settings…", "Ctrl+,", () => OnSettings(null, new RoutedEventArgs()));
        Add("Share Vault…", null, () => OnShareVault(null, new RoutedEventArgs()));
        Add("Join Shared Vault…", null, () => OnJoinVault(null, new RoutedEventArgs()));
        Add("Sync Now", null, () => OnSyncNow(null, new RoutedEventArgs()));
        return list;
    }

    // ---- left rail (Files / Search / Tags) ----

    private DispatcherTimer? _searchDebounce;

    private void OnRailFiles(object? sender, RoutedEventArgs e) { if (Vm is { } v) v.Rail = RailMode.Files; }
    private void OnRailSearch(object? sender, RoutedEventArgs e) => FocusSearchRail();
    private void OnRailTags(object? sender, RoutedEventArgs e) { if (Vm is { } v) { v.RefreshTags(); v.Rail = RailMode.Tags; } }
    private void OnRailBookmarks(object? sender, RoutedEventArgs e) { if (Vm is { } v) { v.RefreshBookmarks(); v.Rail = RailMode.Bookmarks; } }

    private void OnRailOutline(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } v) { RefreshOutline(); v.Rail = RailMode.Outline; }
    }

    private void OnLocalGraph(object? sender, RoutedEventArgs e) => Vm?.OpenLocalGraph();
    private async void OnGlobalGraph(object? sender, RoutedEventArgs e) { if (Vm is { } vm) await vm.OpenGlobalGraphAsync(); }

    // Rebuild the outline from the live editor text (so unsaved edits are reflected); falls back to the saved
    // note when no surface is realized.
    private void RefreshOutline()
    {
        if (Vm is not { } vm) return;
        string text = _activeSurface?.CurrentText
            ?? (vm.CurrentNote is { } p ? vm.ReadNote(p) : "");
        vm.SetOutline(text);
    }

    private void OnOutlinePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: OutlineRow row } && _activeSurface is { } s)
        {
            s.SelectRange(row.Offset, 0);   // caret to the heading line; scrolls it into view
            s.Focus();
            e.Handled = true;
        }
    }

    private void FocusSearchRail()
    {
        if (Vm is not { } v) return;
        v.Rail = RailMode.Search;
        Dispatcher.UIThread.Post(() => SearchBox.Focus());
    }

    // Debounce keystrokes so a big vault isn't searched (each hit note is read for snippets) on every letter.
    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchDebounce ??= CreateSearchDebounce();
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private DispatcherTimer CreateSearchDebounce()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        t.Tick += (_, _) => { t.Stop(); Vm?.RunSearch(SearchBox.Text ?? ""); };
        return t;
    }

    private void OnSearchResultPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: VaultSearchResult r } c) return;
        var p = e.GetCurrentPoint(c).Properties;
        if (p.IsMiddleButtonPressed) Vm?.OpenNote(r.NotePath);   // middle-click → new tab
        else NavigateTo(r.NotePath);                             // left-click → navigate in place
        // The active surface is created + text-loaded synchronously by ShowActiveTab, so it is ready now;
        // land on the term the user searched for instead of the top of the note.
        if (Vm?.LastSearchQuery is { Length: > 0 } q) _activeSurface?.SelectFirstMatch(q);
        e.Handled = true;
    }

    private void OnTagPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: TagRow tag }) { Vm?.ShowTag(tag.Tag); e.Handled = true; }
    }

    private string? _contextTag;

    private void OnTagNodePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: Wiki.Core.Models.TagTreeNode node } c) return;
        _contextTag = node.FullPath;   // remembered for the "Rename tag…" context item
        if (e.GetCurrentPoint(c).Properties.IsLeftButtonPressed)
        {
            Vm?.ShowTagPrefix(node.FullPath);
            e.Handled = true;
        }
    }

    private async void OnTagRename(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || _contextTag is not { } oldTag) return;
        var newTag = await PromptTextAsync("Rename tag", "New tag (no #):", oldTag);
        if (string.IsNullOrWhiteSpace(newTag)) return;
        SaveActiveSurface();                                   // persist the active note before the rewrite
        var changed = vm.RenameTagAt(oldTag, newTag.TrimStart('#').Trim());
        ReloadOpenTabsFromDisk(changed);                       // open tabs' on-disk text just changed
        _ = _sync?.NotifyLocalChangeAsync();                   // propagate to peers
    }

    // Evict the cached surfaces of open note tabs whose file changed on disk (e.g. a vault-wide tag rename)
    // so they reload fresh — otherwise a stale in-memory surface would overwrite the change on its next save.
    private void ReloadOpenTabsFromDisk(IReadOnlyList<string> changedPaths)
    {
        if (Vm is not { } vm || changedPaths.Count == 0) return;
        var changed = new HashSet<string>(changedPaths, StringComparer.Ordinal);
        foreach (var tab in vm.Tabs.Tabs
                     .Where(t => t.Kind == TabKind.Note && t.NotePath is { } p && changed.Contains(p)).ToList())
            _tabControls.Remove(tab);
        if (vm.Tabs.Active is { } active) ShowActiveTab(active);   // recreate the active one now
    }

    private void OnBookmarkPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: string path } c) return;
        var p = e.GetCurrentPoint(c).Properties;
        if (p.IsMiddleButtonPressed) Vm?.OpenNote(path);   // middle-click → new tab
        else NavigateTo(path);
        e.Handled = true;
    }

    private void OnTreeBookmark(object? sender, RoutedEventArgs e)
    {
        if (_contextNode?.NotePath is { } path) Vm?.ToggleBookmark(path);
    }

    private void OnLinkMention(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: UnlinkedMentionRow row } || Vm is not { } vm) return;
        if (vm.LinkMention(row.Mention) is { } changed) ReloadOpenTabsFromDisk(new[] { changed });
    }

    private void OnMentionTitlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: UnlinkedMentionRow row }) { NavigateTo(row.SourceNotePath); e.Handled = true; }
    }

    // Selecting a note in the tree (left-click or keyboard) navigates the active tab in place.
    private void OnNoteSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_revealingInTree) return;   // we set the selection to reveal the active note — don't re-navigate
        if (sender is TreeView { SelectedItem: VaultNode { NotePath: { } path } }) NavigateTo(path);
    }

    // Expand the ancestor folders of the active note and select+scroll it in the Files tree, so
    // navigating via Ctrl+O / a backlink / a wikilink doesn't lose your place in the hierarchy.
    // TreeView expansion depends on realized containers, so this is best-effort and PENDING real-desktop
    // confirmation (the standing headless-host caveat; cf. the pass-3 deferral).
    private void RevealActiveNoteInTree()
    {
        if (_revealingInTree || Vm is not { } vm) return;
        if (vm.Tabs.Active is not { Kind: TabKind.Note, NotePath: { } path }) return;
        var chain = VaultTreeSearch.PathTo(vm.Tree, path);
        if (chain.Count == 0) return;
        _revealingInTree = true;
        try
        {
            // Mark every ancestor folder expanded (containers apply it as they realize) and expand any
            // already-realized ancestor container now.
            var ancestors = new HashSet<VaultNode>(chain.Take(chain.Count - 1));
            foreach (var n in chain) if (n.FolderPath is { } fp) _expandedFolders.Add(fp);
            foreach (var tvi in NotesTree.GetVisualDescendants().OfType<TreeViewItem>())
                if (tvi.DataContext is VaultNode vn && ancestors.Contains(vn)) tvi.IsExpanded = true;
            NotesTree.SelectedItem = chain[^1];
            foreach (var tvi in NotesTree.GetVisualDescendants().OfType<TreeViewItem>())
                if (ReferenceEquals(tvi.DataContext, chain[^1])) { tvi.BringIntoView(); break; }
        }
        finally { _revealingInTree = false; }
    }

    // Plain link click → navigate the current tab; Ctrl/middle-click (newTab) → open a new tab.
    private void OnWikiLinkActivated(string target, bool newTab)
    {
        if (Vm is not { } vm) return;
        if (target.StartsWith('^')) { ScrollToFootnoteDefinition(target[1..]); return; }   // [^id] → its definition
        if (newTab) vm.OpenLinkTarget(target);
        else { SaveActiveSurface(onlyIfDirty: true); vm.NavigateLinkTarget(target); }
    }

    // Scroll the active surface to a footnote's `[^label]:` definition line and place the caret there.
    private void ScrollToFootnoteDefinition(string label)
    {
        if (_activeSurface is not { } surface) return;
        string text = surface.GetText();
        int idx = text.IndexOf("[^" + label + "]:", System.StringComparison.Ordinal);
        if (idx < 0) idx = text.IndexOf("[^" + label + "]:", System.StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) surface.SelectRange(idx, 0);
    }

    // Navigate the active tab to a note, first persisting the current tab's edits (the slot is reused,
    // so unsaved in-memory text would otherwise be dropped).
    private void NavigateTo(string notePath)
    {
        SaveActiveSurface(onlyIfDirty: true);
        Vm?.NavigateActiveTab(notePath);
    }

    // Clicking an image opens it full-size in a new tab.
    private void OnImageActivated(string target)
    {
        var abs = Vm?.ResolveImageAbsolute(target);
        if (!string.IsNullOrEmpty(abs)) Vm?.OpenImageTab(abs!, System.IO.Path.GetFileName(abs!));
    }

    // Backlinks pane: left-click navigates the active tab to the source note (saving the outgoing dirty
    // tab first, via NavigateTo); middle-click opens the source in a new tab.
    private void OnBacklinkPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: BacklinkRow row } c) return;
        var p = e.GetCurrentPoint(c).Properties;
        if (p.IsMiddleButtonPressed) { Vm?.OpenNote(row.NotePath); e.Handled = true; }
        else if (p.IsLeftButtonPressed) { NavigateTo(row.NotePath); e.Handled = true; }
    }

    private void ApplyEditorTheme(ThemeVariant variant)
    {
        bool dark = variant == ThemeVariant.Dark;
        foreach (var c in _tabControls.Values)
            if (c is LivePreviewSurface s) s.ApplyTheme(dark);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SaveActiveSurface();
            e.Handled = true;
        }
        else if (e.Key == Key.N && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            Vm?.NewNote();
            e.Handled = true;
        }
        else if (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (Vm?.Tabs.Active is { } active) _ = Vm.CloseTabAsync(active);   // guarded close
            e.Handled = true;
        }
        else if (e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            OpenQuickSwitcher();
            e.Handled = true;
        }
        else if (e.Key == Key.D && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            Vm?.ToggleBookmarkActive();
            e.Handled = true;
        }
        else if (e.Key == Key.OemBackslash && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) CloseRightPane(); else SplitRight();
            e.Handled = true;
        }
        else if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control)
                 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            FocusSearchRail();
            e.Handled = true;
        }
        else if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Only reached when the editor surface didn't already handle Ctrl+F (e.g. focus is in the tree).
            _activeSurface?.OpenFind();
            e.Handled = true;
        }
        else if (e.Key == Key.OemComma && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            OnSettings(null, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.P && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            Palette.Open();
            e.Handled = true;
        }
        else if (e.Key == Key.OemSemicolon && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _ = EditActiveProperties();
            e.Handled = true;
        }
        else if (e.Key == Key.E && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            OnToggleReadMode(null, new RoutedEventArgs());
            e.Handled = true;
        }
        // Zoom fallbacks: only reached when the surface didn't handle them (focus is off the editor).
        else if (e.Key is Key.OemPlus or Key.Add && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _activeSurface?.ChangeZoom(1);
            e.Handled = true;
        }
        else if (e.Key is Key.OemMinus or Key.Subtract && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _activeSurface?.ChangeZoom(-1);
            e.Handled = true;
        }
        else if (e.Key is Key.D0 or Key.NumPad0 && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _activeSurface?.ResetZoom();
            e.Handled = true;
        }
        else if (e.Key == Key.G && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { if (Vm is { } vm) _ = vm.OpenGlobalGraphAsync(); }
            else Vm?.OpenLocalGraph();
            e.Handled = true;
        }
        else if (e.Key == Key.Left && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            GoBack();
            e.Handled = true;
        }
        else if (e.Key == Key.Right && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            GoForward();
            e.Handled = true;
        }
    }

    private void OpenQuickSwitcher()
    {
        Switcher.PlaceholderText = Vm is { HasVault: true } ? "Search notes…" : "Open a vault first";
        Switcher.Open();
    }

    // Hide the quick switcher and return focus to the active editor surface.
    private void CloseSwitcher()
    {
        Switcher.IsVisible = false;
        _activeSurface?.Focus();
    }

    // ---- tabs ----

    // Wires a pane's tab strip: click activates the tab in THAT pane and focuses it; ✕/middle-click closes.
    private void WireTabStrip(Collective.Platform.Controls.DocumentTabStrip strip, PaneGroupViewModel.Side side)
    {
        strip.TitleSelector = t => ((EditorTab)t).Title;
        strip.ToolTipSelector = t => ((EditorTab)t).NotePath;
        strip.IsDirtySelector = t => ((EditorTab)t).IsDirty;   // renders the unsaved-changes dot
        strip.TabActivated += (_, t) =>
        {
            if (Vm is not { } v) return;
            var pane = side == PaneGroupViewModel.Side.Right ? v.Panes.Right : v.Panes.Left;
            if (pane is null) return;
            pane.Active = (EditorTab)t;
            v.Panes.Focus(side);
        };
        strip.TabCloseRequested += async (_, t) =>
        {
            if (Vm is not { } v) return;
            var pane = side == PaneGroupViewModel.Side.Right ? v.Panes.Right : v.Panes.Left;
            if (pane is not null) await pane.CloseAsync((EditorTab)t);
        };
        strip.TabCloseScopeRequested += async (_, e) =>
        {
            if (Vm is not { } v) return;
            var pane = side == PaneGroupViewModel.Side.Right ? v.Panes.Right : v.Panes.Left;
            if (pane is null) return;
            var anchor = (EditorTab)e.Item;
            switch (e.Scope)
            {
                case Collective.Platform.Controls.TabCloseScope.Others: await pane.CloseOthersAsync(anchor); break;
                case Collective.Platform.Controls.TabCloseScope.Left: await pane.CloseToLeftAsync(anchor); break;
                case Collective.Platform.Controls.TabCloseScope.Right: await pane.CloseToRightAsync(anchor); break;
                case Collective.Platform.Controls.TabCloseScope.All: await pane.CloseAllGuardedAsync(); break;
            }
        };
    }

    // The control for a tab (lazily created, one per tab, shared across panes via _tabControls).
    private Control? ControlFor(EditorTab? tab)
    {
        if (tab is null) return null;
        if (!_tabControls.TryGetValue(tab, out var control)) { control = CreateTabControl(tab); _tabControls[tab] = control; }
        return control;
    }

    // Renders one pane's host + strip selection from its own active tab.
    private void RenderPane(OpenTabsViewModel pane, Border host, Collective.Platform.Controls.DocumentTabStrip strip)
    {
        if (!ReferenceEquals(strip.ItemsSource, pane.Tabs)) strip.ItemsSource = pane.Tabs;
        strip.SelectedItem = pane.Active;
        host.Child = ControlFor(pane.Active);
    }

    // Swap the hosted controls when the active tab (of the active pane) changes; renders BOTH panes and
    // repoints the shared toolbar/find/commands at the active pane's surface.
    private void ShowActiveTab(EditorTab? _)
    {
        if (Vm is not { } vm) return;
        RenderPane(vm.Panes.Left, TabHost, TabStrip);
        if (vm.Panes.Right is { } right) RenderPane(right, TabHostRight, TabStripRight);
        else TabHostRight.Child = null;

        _activeSurface = ControlFor(vm.Panes.Active) as LivePreviewSurface;
        _fmtAdapter.Bind(_activeSurface);   // repoint the shared toolbar at the active surface
        FormatBar.IsEnabled = _activeSurface is not null;   // grey the toolbar out on image tabs
        // Reading view / focus mode are per-tab (each tab keeps its own surface); reflect the active one.
        ReadModeToggle.IsChecked = _activeSurface?.ReadMode == true;
        FocusToggle.IsChecked = _activeSurface?.FocusMode == true;
        (ControlFor(vm.Panes.Active))?.Focus();
        UpdatePaneHighlight();
        if (Vm is { Rail: RailMode.Outline }) RefreshOutline();
        UpdateWordCount();
        RevealActiveNoteInTree();
    }

    // A thin accent border marks the active pane (only meaningful when split).
    private void UpdatePaneHighlight()
    {
        bool split = Vm?.Panes.IsSplit == true;
        bool rightActive = Vm?.Panes.ActiveSide == PaneGroupViewModel.Side.Right;
        PaneLeftBorder.BorderThickness = new Thickness(split && !rightActive ? 2 : 0, 0, 0, 0);
        PaneRightBorder.BorderThickness = new Thickness(split && rightActive ? 2 : 0, 0, 0, 0);
    }

    // The right pane's grid column is collapsed (Width=0) unless a split exists. A star column reserves its
    // share even when its child is hidden, so leaving it "*" strands half the width on the invisible right
    // pane and keeps the editor from filling the window. Opens to an equal split only from the collapsed
    // (absolute) state, so a manual splitter drag survives later re-syncs.
    private void SyncSplitColumn()
    {
        var rightCol = SplitGrid.ColumnDefinitions[2];
        if (Vm?.Panes.IsSplit == true)
        {
            if (rightCol.Width.IsAbsolute)
                rightCol.Width = new GridLength(1, GridUnitType.Star);
        }
        else
        {
            rightCol.Width = new GridLength(0);
        }
    }

    // ---- split view commands ----

    private void SplitRight()
    {
        if (Vm is not { } vm) return;
        var right = vm.Panes.SplitRight();
        SyncSplitColumn();
        if (vm.Panes.Left.Active is { Kind: TabKind.Note, NotePath: { } path })
            right.OpenNote(path, System.IO.Path.GetFileNameWithoutExtension(path), activate: true);
        else ShowActiveTab(null);   // no note to mirror — just reveal the (empty) right pane
    }

    private void CloseRightPane() { Vm?.Panes.CloseRight(); SyncSplitColumn(); }

    private void OpenToSide(string notePath)
    {
        if (Vm is not { } vm) return;
        SaveActiveSurface(onlyIfDirty: true);
        var right = vm.Panes.Right ?? vm.Panes.SplitRight();
        SyncSplitColumn();
        vm.Panes.Focus(PaneGroupViewModel.Side.Right);
        right.OpenNote(notePath, System.IO.Path.GetFileNameWithoutExtension(notePath), activate: true);
    }

    private void OnTreeOpenToSide(object? sender, RoutedEventArgs e)
    {
        if (_contextNode?.NotePath is { } path) OpenToSide(path);
    }

    private void OnSplitRight(object? sender, RoutedEventArgs e) => SplitRight();
    private void OnCloseRightPane(object? sender, RoutedEventArgs e) => CloseRightPane();

    // Live word/char count for the active note (cleared on image/graph tabs).
    private void UpdateWordCount()
    {
        if (this.FindControl<TextBlock>("WordCountText") is not { } t) return;
        if (_activeSurface is { } s)
        {
            var (w, c) = WordCount.Count(s.CurrentText);
            t.Text = $"{w} words · {c} chars";
        }
        else t.Text = "";
    }

    // ---- formatting toolbar ----
    // Each button runs a command on the active note surface, then returns focus so the user keeps typing.
    private void Fmt(Action<LivePreviewSurface> op)
    {
        if (_activeSurface is { } s) { op(s); s.Focus(); }
    }

    // ---- reading view ----

    private void OnReadModeToggled(object? sender, RoutedEventArgs e)
    {
        if (_activeSurface is { } s) { s.ReadMode = ReadModeToggle.IsChecked == true; s.Focus(); }
    }

    private void OnToggleReadMode(object? sender, RoutedEventArgs e)
        => ReadModeToggle.IsChecked = ReadModeToggle.IsChecked != true;   // fires OnReadModeToggled

    // ---- focus mode + zoom (per-tab, on the active surface) ----

    private void OnFocusToggled(object? sender, RoutedEventArgs e)
    {
        if (_activeSurface is { } s) { s.FocusMode = FocusToggle.IsChecked == true; s.Focus(); }
    }

    private void OnToggleFocusMode(object? sender, RoutedEventArgs e)
        => FocusToggle.IsChecked = FocusToggle.IsChecked != true;   // fires OnFocusToggled

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Fmt(s => s.ChangeZoom(1));
    private void OnZoomOut(object? sender, RoutedEventArgs e) => Fmt(s => s.ChangeZoom(-1));
    private void OnZoomReset(object? sender, RoutedEventArgs e) => Fmt(s => s.ResetZoom());

    // Bold/Italic/Strikethrough, headings and lists now live on the shared FormatToolbar (via _fmtAdapter);
    // these are the wiki-specific actions the shared toolbar doesn't carry.
    private void OnFmtHighlight(object? sender, RoutedEventArgs e) => Fmt(s => s.ToggleHighlight());
    private void OnFmtInlineCode(object? sender, RoutedEventArgs e) => Fmt(s => s.ToggleInlineCode());
    private void OnFmtQuote(object? sender, RoutedEventArgs e) => Fmt(s => s.ToggleQuote());
    private void OnFmtLink(object? sender, RoutedEventArgs e) => Fmt(s => s.InsertWikiLink());
    private void OnFmtCodeBlock(object? sender, RoutedEventArgs e) => Fmt(s => s.InsertCodeBlock());

    private Control CreateTabControl(EditorTab tab)
    {
        if (tab.Kind == TabKind.Image && tab.ImagePath is { } img)
            return new ImageViewer(img);

        if (tab.Kind == TabKind.Graph && tab.Graph is { } graph)
            return new GraphView(graph, p => Vm?.OpenNote(p), tab.GraphCenter, tab.GraphPositions);

        var surface = new LivePreviewSurface
        {
            ImageResolver = Vm!.ResolveImageAbsolute,
            ImageSaver = (bytes, ext) => Vm?.SaveClipboardImage(bytes, ext),
            LinkCandidates = q => Vm?.LinkCandidates(q) ?? Array.Empty<string>(),
            TagCandidates = q => Vm?.TagCandidates(q) ?? Array.Empty<string>(),
        };
        surface.WikiLinkActivated += OnWikiLinkActivated;
        surface.ImageActivated += OnImageActivated;
        surface.SaveRequested += (_, _) => SaveActiveSurface();
        surface.PropertiesEditRequested += async (_, _) => await EditActiveProperties();
        surface.SlashCommandInvoked += id => { if (id == "template") _ = InsertTemplateAsync(); };
        surface.FindRequested += (_, seed) => NoteFindBar.Attach(_activeSurface, seed);
        surface.ReplaceRequested += (_, seed) => NoteFindBar.Attach(_activeSurface, seed, showReplace: true);
        surface.TextChanged += (_, _) =>
        {
            tab.IsDirty = true; UpdateWordCount(); ScheduleAutosave();
            if (Vm is { Rail: RailMode.Outline }) RefreshOutline();   // keep the Outline rail live as you type
        };
        surface.ApplyTheme(ActualThemeVariant == ThemeVariant.Dark);
        if (tab.NotePath is { } path) surface.SetText(Vm!.ReadNote(path));
        return surface;
    }

    private void SaveActiveSurface(bool onlyIfDirty = false)
    {
        if (_activeSurface is null) return;
        if (onlyIfDirty && Vm?.Tabs.Active?.IsDirty != true) return;
        Vm?.SaveActive(_activeSurface.GetText());
        _ = _sync?.NotifyLocalChangeAsync();   // reflect the edit into the sync replica (off-thread)
    }

    private void UpdateSyncStatusText()
    {
        // Read the state for any live sync host (joined-but-not-serving vaults still have one), not just
        // while sharing — so a collaborator sees "Synced 14:32 · 1 peer" / "Offline — will retry" too.
        if (this.FindControl<TextBlock>("SyncStatusText") is { } text)
            text.Text = _sync is { } s ? "⇄ " + SyncStatusFormatter.Summarize(s.Status, s.LastSyncedAt, s.OnlinePeers) : "";
    }

    // Show/hide the status-bar conflict counter as the vault's conflicted-copy count changes.
    private void UpdateConflictButton()
    {
        if (this.FindControl<Button>("ConflictButton") is not { } btn) return;
        int n = Vm?.ConflictCount ?? 0;
        btn.IsVisible = n > 0;
        btn.Content = $"⚠ {n} conflict{(n == 1 ? "" : "s")}";
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ConflictCount)) UpdateConflictButton();
    }

    // ---- Sync menu ----

    private async void OnShareVault(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { HasVault: true }) { await MessageAsync("Sync", "Open a vault first."); return; }
        if (await EnsureSyncAsync() is not { } sync) { await MessageAsync("Sync", "Could not initialize sync for this vault."); return; }

        // First-share identity, captured inline (prefilled) rather than via two up-front prompts. The fields
        // show until this vault has an owner; ShareVault(name, email) runs on the first Add-collaborator click.
        var nameBox = new TextBox { Text = _settings.SyncDeviceName ?? "", PlaceholderText = "Your name" };
        var emailBox = new TextBox { Text = _settings.SyncDeviceEmail ?? "", PlaceholderText = "Email", Margin = new Thickness(10, 0, 0, 0) };
        var identityGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        Grid.SetColumn(nameBox, 0);
        Grid.SetColumn(emailBox, 1);
        identityGrid.Children.Add(nameBox);
        identityGrid.Children.Add(emailBox);
        var identityPanel = new StackPanel
        {
            Spacing = 6, IsVisible = !sync.IsOwner,
            Children = { new TextBlock { Text = "You (shown to collaborators)", FontWeight = FontWeight.Bold }, identityGrid },
        };

        var list = new ListBox
        {
            ItemsSource = sync.Collaborators,
            Height = 170,
            ItemTemplate = new FuncDataTemplate<CollaboratorRow>((row, _) =>
            {
                var revoke = new Button { Content = "Revoke", Margin = new Thickness(8, 0, 0, 0) };
                revoke.Click += (_, _) => sync.Revoke(row.DeviceId);
                return new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = row.Name, MinWidth = 120, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = row.Email, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = row.Role.ToString(), Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center },
                        revoke,
                    },
                };
            }),
        };
        var role = new ComboBox { ItemsSource = new[] { PeerRole.ReadWrite, PeerRole.ReadOnly }, SelectedIndex = 0, Width = 160 };
        var add = new Button { Content = "Add collaborator + copy invite" };
        var hintText = new TextBlock { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap,
            Text = "Add a collaborator to start sharing and copy an invite." };
        add.Click += async (_, _) =>
        {
            // First share: establish ownership from the inline identity before we can mint invites.
            if (!sync.IsOwner)
            {
                var nm = nameBox.Text?.Trim();
                var em = emailBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(nm) || string.IsNullOrWhiteSpace(em))
                {
                    hintText.Text = "Enter your name and email first.";
                    return;
                }
                await sync.ShareVaultAsync(nm, em);
                _settings.SyncDeviceName = nm;
                _settings.SyncDeviceEmail = em;
                identityPanel.IsVisible = false;
            }
            try
            {
                if (!sync.IsSharing) sync.StartServing(_settings.PairingPort, _settings.SyncPort, _settings.InternetSyncEnabled);
            }
            catch (SocketException)
            {
                // Another window (or app) already serves these ports — surface it instead of crashing the handler.
                hintText.Text = "Could not start sharing: the sync ports are already in use (another window sharing?).";
                return;
            }
            _settings.SyncEnabled = true; SaveSettings(); UpdateSyncStatusText();

            var invite = sync.AddCollaborator((PeerRole)role.SelectedItem!, TimeSpan.FromHours(24));
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clip) await clip.SetTextAsync(invite);
            hintText.Text = "Invite copied to clipboard — send it to your collaborator (valid 24h).";
        };

        var dlg = new Window
        {
            Title = "Share Vault", Width = 470, Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(18), Spacing = 10,
                Children =
                {
                    identityPanel,
                    new TextBlock { Text = "Collaborators", FontWeight = FontWeight.Bold }, list,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { role, add } },
                    hintText,
                },
            },
        };
        await dlg.ShowDialog(this);
    }

    // Manual pull the view-model always had but no UI reached: refresh from disk, then pull every known peer.
    private async void OnSyncNow(object? sender, RoutedEventArgs e)
    {
        if (await EnsureSyncAsync() is { } s) await s.SyncNowAsync();
    }

    private void OnStopSharing(object? sender, RoutedEventArgs e)
    {
        _sync?.StopServing();
        _settings.SyncEnabled = false;
        SaveSettings();
        UpdateSyncStatusText();
    }

    // The status-bar conflict counter opens the first conflicted copy so today's conflicts are reachable
    // (an in-app merge view is a backlog successor).
    private void OnConflictsClick(object? sender, RoutedEventArgs e)
    {
        if (Vm?.FirstConflictPath is { } p) Vm.OpenNote(p);
    }

    // One consolidated dialog (folder + invite + prefilled identity + collapsed Advanced override) replaces
    // the old five sequential prompts. The dialog owns validation + inline progress + friendly outcome
    // messages; this delegate does the same open/rebuild-sync/pair sequence the prompt chain used to.
    private async void OnJoinVault(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        // Joining opens (switches to) the chosen vault even on a failed pairing, which clears the current
        // vault's tabs — so guard unsaved work before the dialog, aborting the join if the user cancels.
        if (!await PromptSaveDirtyNoteTabsAsync()) return;
        await JoinVaultDialog.ShowAsync(this, _settings.LastVaultPath, _settings.SyncDeviceName, _settings.SyncDeviceEmail,
            async form =>
            {
                _settings.LastVaultPath = form.Folder;
                _settings.SyncDeviceName = form.Name;
                _settings.SyncDeviceEmail = form.Email;
                SaveSettings();

                await vm.OpenVaultAsync(form.Folder);
                await _recentVaults.AddAsync(form.Folder);
                await RefreshRecentVaultsAsync();
                _sync?.Dispose();
                _sync = null;
                UpdateSyncStatusText();
                if (await EnsureSyncAsync() is not { } sync) return PairingOutcome.WrongVault;
                if (!sync.IsSharing) sync.StartServing(_settings.PairingPort, _settings.SyncPort, _settings.InternetSyncEnabled);

                var outcome = await sync.JoinAsync(form.Invite, form.Name, form.Email, form.HostOverride);
                if (outcome == PairingOutcome.Accepted)
                {
                    _settings.SyncEnabled = true;
                    SaveSettings();
                    UpdateSyncStatusText();
                }
                return outcome;
            });
    }

    // First share/join prompts for name + email (git-style); persisted in settings and reused thereafter.
    private async Task<(string name, string email)?> EnsureIdentityAsync()
    {
        if (!string.IsNullOrWhiteSpace(_settings.SyncDeviceName) && !string.IsNullOrWhiteSpace(_settings.SyncDeviceEmail))
            return (_settings.SyncDeviceName!, _settings.SyncDeviceEmail!);
        var name = await PromptTextAsync("Your name", "Display name for collaborators:", _settings.SyncDeviceName ?? "");
        if (string.IsNullOrWhiteSpace(name)) return null;
        var email = await PromptTextAsync("Your email", "Email (git-style identity):", _settings.SyncDeviceEmail ?? "");
        if (string.IsNullOrWhiteSpace(email)) return null;
        _settings.SyncDeviceName = name; _settings.SyncDeviceEmail = email; SaveSettings();
        return (name, email);
    }

    private async Task<string?> PromptTextAsync(string title, string prompt, string initial = "")
    {
        var box = new TextBox { Text = initial, Width = 380 };
        string? result = null;
        var ok = new Button { Content = "OK", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var dlg = new Window
        {
            Title = title, Width = 420, Height = 160, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(18), Spacing = 10,
                Children =
                {
                    new TextBlock { Text = prompt }, box,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { ok, cancel },
                    },
                },
            },
        };
        ok.Click += (_, _) => { result = box.Text; dlg.Close(); };
        cancel.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
        return result;
    }

    private async Task MessageAsync(string title, string body)
    {
        var dlg = new Window
        {
            Title = title, Width = 380, Height = 150, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(18), Spacing = 12,
                Children = { new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap } },
            },
        };
        await dlg.ShowDialog(this);
    }

    private void OnTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (var obj in e.OldItems)
                if (obj is EditorTab tab) { tab.PropertyChanged -= OnTabPropertyChanged; _tabControls.Remove(tab); }
        // Watch each tab's IsDirty so the strip's dirty dot appears/clears as the note is edited/saved.
        if (e.NewItems is not null)
            foreach (var obj in e.NewItems)
                if (obj is EditorTab tab) tab.PropertyChanged += OnTabPropertyChanged;
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorTab.IsDirty)) TabStrip.Refresh();
    }

    // Tree note interactions: left-click selects + navigates the current tab (OnNoteSelected);
    // middle-click opens a NEW tab; right-click remembers the node for the context-menu commands.
    private VaultNode? _dragArm;                  // armed on left-press, promoted to a drag past the threshold
    private Point _dragStart;
    private PointerPressedEventArgs? _dragPress;  // the press that started the gesture (DoDragDropAsync needs it)
    private VaultNode? _draggingNode;             // the in-process drag payload (same window → a field, not serialized)

    private void OnTreeItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: VaultNode node } c) return;
        var p = e.GetCurrentPoint(c).Properties;
        if (p.IsMiddleButtonPressed && node.NotePath is { } mid) { Vm?.OpenNote(mid); e.Handled = true; }
        else if (p.IsRightButtonPressed) _contextNode = node;
        else if (p.IsLeftButtonPressed) { _dragArm = node; _dragStart = e.GetPosition(this); _dragPress = e; }
    }

    // Starts a drag once the pointer moves past a small threshold with the left button held. Moving a
    // note/folder onto a folder calls the existing link-preserving Move cores. On-screen PENDING real desktop.
    private async void OnTreeItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragArm is not { } node || _dragPress is not { } press) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _dragArm = null; _dragPress = null; return; }
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _dragStart.X) < 6 && Math.Abs(pos.Y - _dragStart.Y) < 6) return;
        _dragArm = null; _dragPress = null;
        _draggingNode = node;
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText(node.IsNote ? node.NotePath ?? "" : node.FolderPath ?? ""));
        try { await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Move); }
        finally { _draggingNode = null; }
    }

    private void OnTreeDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = CanDropOnTree(e) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTreeDrop(object? sender, DragEventArgs e)
    {
        if (Vm is not { } vm || _draggingNode is not { } src) return;
        if (DropTargetFolder(e) is not { } target) return;
        if (src.IsNote && src.NotePath is { } np) vm.MoveNoteToFolder(np, target);
        else if (!src.IsNote && src.FolderPath is { } sf && target != sf) vm.MoveFolderToParent(sf, target);
        else return;
        _ = _sync?.NotifyLocalChangeAsync();
        e.Handled = true;
    }

    private bool CanDropOnTree(DragEventArgs e)
    {
        if (_draggingNode is not { } src) return false;
        if (DropTargetFolder(e) is not { } target) return false;
        if (!src.IsNote && src.FolderPath is { } sf)   // a folder can't move into itself or a descendant
            return target != sf && !target.StartsWith(sf + "/", StringComparison.Ordinal);
        return true;
    }

    // The folder a drop lands in: the folder node under the pointer, a note's parent folder, else the root.
    private string? DropTargetFolder(DragEventArgs e)
    {
        if (e.Source is Visual v)
        {
            var node = v.GetSelfAndVisualAncestors().OfType<Control>()
                .Select(c => c.DataContext).OfType<VaultNode>().FirstOrDefault();
            if (node is not null)
                return node.IsNote
                    ? (node.NotePath!.LastIndexOf('/') is var s && s >= 0 ? node.NotePath[..s] : "")
                    : node.FolderPath;
        }
        return "";   // empty tree space → vault root
    }

    // Right-click context menu: "Open" reuses the current tab; "Open in New Tab" opens a new one.
    private void OnTreeOpen(object? sender, RoutedEventArgs e)
    {
        if (_contextNode?.NotePath is { } path) NavigateTo(path);
    }

    private void OnTreeOpenNewTab(object? sender, RoutedEventArgs e)
    {
        if (_contextNode?.NotePath is { } path) Vm?.OpenNote(path);
    }

    // Rename / Delete act on the last right-clicked node (context menu) or the selected node (F2 / Del),
    // notes only. The Core rename rewrites inbound links + keeps the index consistent; both flow into sync.
    private void OnTreeRename(object? sender, RoutedEventArgs e) => _ = RenameNodeAsync(_contextNode);
    private void OnTreeDelete(object? sender, RoutedEventArgs e) => _ = DeleteNodeAsync(_contextNode);

    // ---- folder + move commands ----

    private void OnTreeNewNoteHere(object? sender, RoutedEventArgs e) => _ = NewNoteInAsync(_contextNode?.FolderPath ?? "");
    private void OnTreeNewFolder(object? sender, RoutedEventArgs e) => _ = NewFolderInAsync(_contextNode?.FolderPath ?? "");

    // Right-click on empty rail space (the TreeView background, not an item): create at the vault root.
    // Ignores _contextNode so a stale right-click on a folder can't retarget these.
    private void OnRailNewNote(object? sender, RoutedEventArgs e) => _ = NewNoteInAsync("");
    private void OnRailNewFolder(object? sender, RoutedEventArgs e) => _ = NewFolderInAsync("");
    private void OnFolderRename(object? sender, RoutedEventArgs e) => _ = RenameFolderAsync(_contextNode);
    private void OnFolderDelete(object? sender, RoutedEventArgs e) => _ = DeleteFolderAsync(_contextNode);
    private void OnTreeMove(object? sender, RoutedEventArgs e) => _ = MoveNodeAsync(_contextNode);

    private async Task NewNoteInAsync(string folder)
    {
        if (Vm is not { } vm) return;
        var name = await PromptTextAsync("New note", "Note name:", "Untitled");
        if (string.IsNullOrWhiteSpace(name)) return;
        vm.CreateNoteIn(folder, name);
        _ = _sync?.NotifyLocalChangeAsync();
    }

    private async Task NewFolderInAsync(string parent)
    {
        if (Vm is not { } vm) return;
        var name = await PromptTextAsync("New folder", "Folder name:", "New Folder");
        if (string.IsNullOrWhiteSpace(name)) return;
        vm.CreateFolderIn(parent, name);
    }

    private async Task RenameFolderAsync(VaultNode? node)
    {
        if (node is not { IsNote: false, FolderPath: { } folder } || Vm is not { } vm) return;
        int slash = folder.LastIndexOf('/');
        string current = slash >= 0 ? folder[(slash + 1)..] : folder;
        var name = await PromptTextAsync("Rename folder", "New name:", current);
        if (string.IsNullOrWhiteSpace(name)) return;
        vm.RenameFolderAt(folder, name);
        _ = _sync?.NotifyLocalChangeAsync();
    }

    private async Task DeleteFolderAsync(VaultNode? node)
    {
        if (node is not { IsNote: false, FolderPath: { } folder, Name: { } folderName } || Vm is not { } vm) return;
        if (await ConfirmDialog.ShowAsync(this, "Delete folder",
                $"Delete “{folderName}” and everything inside it? The notes are removed from the vault (and synced copies).",
                confirmText: "Delete", destructive: true))
        {
            vm.DeleteFolderAt(folder);
            _ = _sync?.NotifyLocalChangeAsync();
        }
    }

    // Move a note or folder to another folder chosen from the vault's existing folders (recognition over
    // recall — no blind path typing). The picker lists real folders, so a move can only ever target a
    // folder that already exists.
    private async Task MoveNodeAsync(VaultNode? node)
    {
        if (node is null || Vm is not { } vm) return;

        if (node.NotePath is { } notePath) { await MoveNoteAsync(notePath); return; }

        if (node.FolderPath is { } folderPath)
        {
            // A folder cannot be moved into itself or its own subtree — hide those targets up front.
            var folder = await PickFolderAsync(excludeSubtreeOf: folderPath.TrimEnd('/'));
            if (folder is null) return;
            vm.MoveFolderToParent(folderPath, folder);
            _ = _sync?.NotifyLocalChangeAsync();
        }
    }

    // Folder picker over the vault's existing folders; the first entry is the vault root ("").
    // Returns the picked '/'-relative folder, or null when cancelled.
    private async Task<string?> PickFolderAsync(string? excludeSubtreeOf)
    {
        if (Vm is not { } vm) return null;
        const string rootLabel = "(vault root)";
        var choices = new List<string> { rootLabel };
        foreach (var f in vm.FolderPaths())
        {
            if (excludeSubtreeOf is not null
                && (f == excludeSubtreeOf || f.StartsWith(excludeSubtreeOf + "/", StringComparison.Ordinal))) continue;
            choices.Add(f);
        }
        var picked = await ChooseAsync("Move to folder", choices);
        return picked is null ? null : picked == rootLabel ? "" : picked;
    }

    // ---- Copy Wikilink / Reveal in Explorer ----

    private void OnTreeCopyLink(object? sender, RoutedEventArgs e)
    {
        if (_contextNode?.NotePath is { } p) CopyWikilink(p);
    }

    private void CopyWikilink(string notePath)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
            _ = cb.SetTextAsync("[[" + System.IO.Path.GetFileNameWithoutExtension(notePath) + "]]");
    }

    private void OnTreeReveal(object? sender, RoutedEventArgs e)
    {
        string? rel = _contextNode?.NotePath ?? _contextNode?.FolderPath;
        if (rel is not null) RevealInExplorer(rel);
    }

    // Opens the OS file manager with the vault-relative path selected (its folder, on Linux).
    private void RevealInExplorer(string relPath)
    {
        if (Vm?.VaultRoot is not { } root) return;
        string abs = System.IO.Path.Combine(root, relPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        try
        {
            // ArgumentList (not a formatted Arguments string): each entry is ONE argv element, so a
            // note/folder name containing quotes or spaces can't split into extra arguments or flags.
            System.Diagnostics.ProcessStartInfo psi;
            if (OperatingSystem.IsWindows())
            {
                psi = new("explorer.exe") { UseShellExecute = false };
                psi.ArgumentList.Add("/select," + abs);
            }
            else if (OperatingSystem.IsMacOS())
            {
                psi = new("open") { UseShellExecute = false };
                psi.ArgumentList.Add("-R");
                psi.ArgumentList.Add(abs);
            }
            else
            {
                string dir = System.IO.Directory.Exists(abs) ? abs : System.IO.Path.GetDirectoryName(abs) ?? root;
                psi = new("xdg-open") { UseShellExecute = false };
                psi.ArgumentList.Add(dir);
            }
            System.Diagnostics.Process.Start(psi);
        }
        catch { /* a missing shell handler must never crash the app */ }
    }

    private void OnNotesTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (NotesTree.SelectedItem is not VaultNode { NotePath: not null } node) return;
        if (e.Key == Key.F2) { e.Handled = true; _ = RenameNodeAsync(node); }
        else if (e.Key == Key.Delete) { e.Handled = true; _ = DeleteNodeAsync(node); }
    }

    private Task RenameNodeAsync(VaultNode? node)
        => node is { NotePath: { } path } ? RenameNoteAsync(path) : Task.CompletedTask;

    private Task DeleteNodeAsync(VaultNode? node)
        => node is { NotePath: { } path } ? DeleteNoteAsync(path) : Task.CompletedTask;

    private async Task RenameNoteAsync(string path)
    {
        if (Vm is not { } vm) return;
        var newName = await PromptTextAsync("Rename note", "New name:", System.IO.Path.GetFileNameWithoutExtension(path));
        if (string.IsNullOrWhiteSpace(newName)) return;
        vm.RenameNoteAt(path, newName);
        _ = _sync?.NotifyLocalChangeAsync();   // propagate the rename to peers
    }

    private async Task DeleteNoteAsync(string path)
    {
        if (Vm is not { } vm) return;
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        if (await ConfirmDialog.ShowAsync(this, "Delete note",
                $"Delete “{name}”? The file is removed from the vault (and from synced copies).",
                confirmText: "Delete", destructive: true))
        {
            vm.DeleteNoteAt(path);
            _ = _sync?.NotifyLocalChangeAsync();   // deletion propagates as a sync tombstone
        }
    }

    private async Task MoveNoteAsync(string path)
    {
        if (Vm is not { } vm) return;
        var folder = await PickFolderAsync(excludeSubtreeOf: null);
        if (folder is null) return;
        vm.MoveNoteToFolder(path, folder);
        _ = _sync?.NotifyLocalChangeAsync();
    }

    // ---- Settings (Ctrl+,) ----

    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        // The dialog mutates _settings in place, so capture the internet-sync posture before it opens to tell
        // whether the user toggled it.
        var wasInternetSync = _settings.InternetSyncEnabled;
        if (await SettingsWindow.ShowAsync(this, _settings))
        {
            SaveSettings();
            ApplyThemeMode(_settings.ThemeMode);
            Vm?.ApplyPreferences(_settings.AttachmentsFolder, _settings.TemplatesFolder);
            if (_settings.InternetSyncEnabled != wasInternetSync)
                await ApplyInternetSyncChangeAsync();
        }
    }

    // Re-bind the sync listeners so an internet-sync toggle takes effect immediately, without an app restart.
    // Turning it ON re-binds dual-stack and opens the firewall + UPnP mapping (via StartServing's reachability
    // work); turning it OFF undoes that posture — drains any in-flight reachability work, releases the router
    // mapping and the firewall admission — and re-binds IPv4-only so LAN sync keeps working while the
    // internet-facing surface is closed. The undo runs even when not currently sharing: a firewall rule or
    // router mapping can outlive the serve (or this session) and turning the setting off must close it. The
    // peer set survives the stop/start (StopServing keeps it), so collaborators are not dropped.
    private async Task ApplyInternetSyncChangeAsync()
    {
        var removed = true;
        if (_sync is { } sync)
        {
            var wasSharing = sync.IsSharing;
            if (wasSharing) sync.StopServing();
            if (!_settings.InternetSyncEnabled) removed = await sync.RemoveInternetReachabilityAsync();
            if (wasSharing)
            {
                try { sync.StartServing(_settings.PairingPort, _settings.SyncPort, _settings.InternetSyncEnabled); }
                catch (SocketException) { /* another window holds the port — leave this vault un-shared, as elsewhere */ }
            }
        }
        else if (!_settings.InternetSyncEnabled)
        {
            // No sync host stood up this session, but a firewall rule / router mapping may persist from an
            // earlier one — turning the setting off must still close them. The blind unmap assumes the
            // default same-number external ports an earlier session's mapping used (best-effort either way;
            // building a full sync host — device identity, sidecars — just to undo this would be worse).
            var mapper = new MonoNatPortMapper();
            await mapper.TryUnmapAsync(_settings.PairingPort, _settings.PairingPort, TimeSpan.FromSeconds(5), CancellationToken.None);
            await mapper.TryUnmapAsync(_settings.SyncPort, _settings.SyncPort, TimeSpan.FromSeconds(5), CancellationToken.None);
            removed = await FirewallOpeners.CreateDefault().RemoveAsync(CancellationToken.None);
        }
        if (!removed)
            await MessageAsync("Internet sync",
                "The firewall rule could not be removed (was the elevation prompt declined?), so this device may " +
                "still be reachable from the internet. Turn internet sync off again to retry, or remove the rule " +
                $"\"{NetshFirewallOpener.RuleName}\" in Windows Defender Firewall.");
        UpdateSyncStatusText();
    }

    private async void OnEditProperties(object? sender, RoutedEventArgs e) => await EditActiveProperties();

    // Open the typed Properties editor for the active note and write the rebuilt front-matter back.
    private async Task EditActiveProperties()
    {
        if (_activeSurface is not { } surface) return;
        string? updated = await PropertiesEditorDialog.ShowAsync(this, surface.CurrentText);
        if (updated is not null && updated != surface.CurrentText) surface.ReplaceDocument(updated);
    }

    // ---- View ▸ Theme ----

    private void OnThemeSystem(object? sender, RoutedEventArgs e) => ApplyThemeMode("System");
    private void OnThemeLight(object? sender, RoutedEventArgs e) => ApplyThemeMode("Light");
    private void OnThemeDark(object? sender, RoutedEventArgs e) => ApplyThemeMode("Dark");

    private void ApplyThemeMode(string mode, bool persist = true)
    {
        _themeMode = mode;
        ThemeController.Apply(mode);
        ThemeSystemItem.IsChecked = mode == "System";
        ThemeLightItem.IsChecked = mode == "Light";
        ThemeDarkItem.IsChecked = mode == "Dark";
        if (persist) { _settings.ThemeMode = mode; SaveSettings(); }
    }

    // ---- Help ----

    private void OnAbout(object? sender, RoutedEventArgs e)
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString();
        _ = AboutDialog.ShowAsync(this, new AboutDialogModel("CollectiveWiki", version,
            "A local-first, linked Markdown knowledge base.",
            new[] { "Part of the Collective Software suite.", "GPL-3.0-or-later." }));
    }

    // ---- Auto-update (Plan 2) ----

    // Built lazily: the key-agnostic service gets this app's feed/key/RID/version + real HTTP and applier.
    private UpdateCoordinator Updates()
    {
        if (_updates is not null) return _updates;
        var appData = new DesktopFileSystem("CollectiveWiki").AppDataDirectory;
        var stageDir = System.IO.Path.Combine(appData, "updates");
        var feed = UpdateConfig.BuildFeed(UpdateConfig.CurrentVersion(), UpdateConfig.CurrentRid(), _settings.SkippedVersion);
        var applier = new FileSwapApplier(
            launch: path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = false }),
            exit: code => Environment.Exit(code));
        var service = new UpdateService(feed, new HttpUpdateDownloader(_updateHttp), applier, stageDir);
        _updates = new UpdateCoordinator(service, _settings, SaveSettings);
        return _updates;
    }

    // Startup: honour the consent gate, then (Automatic + due) check quietly. Failures stay silent here.
    private async Task MaybeCheckForUpdatesAtStartupAsync()
    {
        var coord = Updates();
        var decision = coord.DecideAuto(DateTime.UtcNow);
        if (decision == AutoDecision.NeedConsent)
        {
            var choice = await UpdateConsentDialog.ShowAsync(this);
            if (choice == UpdateConsentChoice.NotNow) return;            // stays Unset; asked again next launch
            coord.RecordConsent(choice == UpdateConsentChoice.Automatic);
            if (choice == UpdateConsentChoice.Manual) return;           // no check until they ask
            decision = coord.DecideAuto(DateTime.UtcNow);              // now Automatic -> CheckNow
        }
        if (decision != AutoDecision.CheckNow) return;
        try
        {
            var result = await coord.CheckAsync(DateTime.UtcNow, default);
            if (result is UpdateCheck.Available a) await OfferUpdateAsync(a.Info);
            // Automatic checks are silent on UpToDate/Failed (spec §6) — no dialog.
        }
        catch { /* automatic: swallow; a manual check surfaces failures */ }
    }

    // Help ▸ Check for Updates — always reports a result, prompting for consent first if still Unset.
    private async void OnCheckForUpdates(object? sender, RoutedEventArgs e)
    {
        var coord = Updates();
        if (_settings.UpdateCheckMode == "Unset")
        {
            var choice = await UpdateConsentDialog.ShowAsync(this);
            if (choice == UpdateConsentChoice.NotNow) return;
            coord.RecordConsent(choice == UpdateConsentChoice.Automatic);
        }
        UpdateCheck result;
        try { result = await coord.CheckAsync(DateTime.UtcNow, default); }
        catch (Exception ex) { await InfoAsync("Update check", "Couldn't check for updates: " + ex.Message); return; }

        switch (result)
        {
            case UpdateCheck.Available a: await OfferUpdateAsync(a.Info); break;
            case UpdateCheck.UpToDate: await InfoAsync("Update check", "You're on the latest version."); break;
            case UpdateCheck.Failed f: await InfoAsync("Update check", "Couldn't check for updates: " + f.Reason); break;
        }
    }

    private async Task OfferUpdateAsync(UpdateInfo info)
    {
        var choice = await UpdateAvailableDialog.ShowAsync(this, info.Version, info.NotesUrl);
        if (choice == UpdateChoice.Skip) { Updates().SkipVersion(info.Version); _updates = null; return; }
        if (choice != UpdateChoice.UpdateNow) return;

        if (!await PromptSaveDirtyNoteTabsAsync()) return;                // save open work before the restart

        // The download is a visible operation: a live progress bar the user can cancel, not a silent
        // pause before the app suddenly restarts.
        var result = await UpdateProgressDialog.RunAsync(this, Updates(), info);
        if (result.Cancelled) return;                                    // user stopped it; stay on this version
        if (result.Error is not null) { await InfoAsync("Update", "Download failed: " + result.Error); return; }
        if (result.Staged is null) { await InfoAsync("Update", "The download could not be verified and was discarded."); return; }

        var outcome = Updates().Apply(result.Staged, UpdateConfig.CurrentExePath());
        // Apply restarts the process on success; reaching here means it did not.
        await InfoAsync("Update", outcome == ApplyOutcome.NotWritable
            ? "This install location is not writable. Download the new version from the releases page."
            : "The update could not be applied. Your current version is unchanged.");
    }

    private Task InfoAsync(string title, string message)
        => AboutDialog.ShowAsync(this, new AboutDialogModel(title, null, message, System.Array.Empty<string>()));

    // ---- Window state ----
    // Geometry restore-before-show + persist-on-close is now handled by WindowStateService
    // (wired in App.axaml.cs), backed by its own "window" settings blob.

    private bool _closeConfirmed;

    // Prompts Save / Don't save / Cancel for each dirty note tab. Returns false if the user cancelled
    // (the caller aborts its action). Runs while _session still points at the current vault, so SaveTab
    // writes to the right place — shared by window-close and vault-switch.
    private async Task<bool> PromptSaveDirtyNoteTabsAsync()
    {
        if (Vm is not { } vm) return true;
        foreach (var tab in vm.Tabs.Tabs.Where(t => t is { IsDirty: true, Kind: TabKind.Note }).ToArray())
        {
            var r = await SaveChangesDialog.ShowAsync(this, $"Save changes to “{tab.Title}”?");
            if (r == SaveChangesResult.Cancel) return false;
            if (r == SaveChangesResult.Save && _tabControls.TryGetValue(tab, out var c) && c is LivePreviewSurface s)
                vm.SaveTab(tab, s.GetText());
        }
        return true;
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Guard unsaved work first. If any note tab is dirty and we haven't already run the prompts, cancel
        // this close, ask Save / Don't save / Cancel per dirty tab, then close for real. e.Cancel is set
        // synchronously (before the first await) so Avalonia honours it; Close() re-enters with the flag set.
        if (!_closeConfirmed && Vm is { } gvm &&
            gvm.Tabs.Tabs.Any(t => t is { IsDirty: true, Kind: TabKind.Note }))
        {
            e.Cancel = true;
            if (!await PromptSaveDirtyNoteTabsAsync()) return;   // cancelled — window stays open
            _closeConfirmed = true;
            Close();
            return;
        }

        _settings.ThemeMode = _themeMode;
        _sync?.Dispose();
        if (Vm is { } vm) _settings.BacklinksVisible = vm.BacklinksVisible;
        SaveSettings();
    }

    private void SaveSettings() => _store?.SaveAsync("settings", _settings).GetAwaiter().GetResult();
}
