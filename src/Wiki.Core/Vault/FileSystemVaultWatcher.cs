// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Vault;

/// <summary>The production <see cref="IVaultWatcher"/>: a recursive <see cref="FileSystemWatcher"/> over
/// the vault's '*.md' files, feeding a <see cref="WatchDebouncer"/> that a timer drains. Events under
/// dot-directories (e.g. .cwiki) are ignored. The hard coalescing logic lives in the (pure, tested)
/// debouncer; this class is the thin host wiring.</summary>
public sealed class FileSystemVaultWatcher : IVaultWatcher
{
    private readonly string _root;
    private readonly int _debounceMs;
    private readonly WatchDebouncer _debouncer = new();
    private FileSystemWatcher? _fsw;
    private Timer? _timer;

    public event EventHandler<VaultChange>? Changed;

    public FileSystemVaultWatcher(string vaultRoot, int debounceMs = 250)
        => (_root, _debounceMs) = (Path.GetFullPath(vaultRoot), debounceMs);

    public void Start()
    {
        _fsw = new FileSystemWatcher(_root, "*.md")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _fsw.Created += (_, e) => Record(VaultChangeKind.Added, e.FullPath);
        _fsw.Changed += (_, e) => Record(VaultChangeKind.Modified, e.FullPath);
        _fsw.Deleted += (_, e) => Record(VaultChangeKind.Deleted, e.FullPath);
        _fsw.Renamed += (_, e) => Record(VaultChangeKind.Renamed, e.FullPath, e.OldFullPath);
        _fsw.EnableRaisingEvents = true;

        _timer = new Timer(_ => Flush(), null, _debounceMs, _debounceMs);
    }

    private void Record(VaultChangeKind kind, string fullPath, string? oldFullPath = null)
    {
        if (IsHidden(fullPath)) return;
        string path = ToRelative(_root, fullPath);
        string? oldPath = oldFullPath is null ? null : ToRelative(_root, oldFullPath);
        lock (_debouncer) _debouncer.Observe(new VaultChange(kind, path, oldPath), Environment.TickCount64);
    }

    private void Flush()
    {
        IReadOnlyList<VaultChange> ready;
        lock (_debouncer) ready = _debouncer.Drain(Environment.TickCount64, _debounceMs);
        foreach (var change in ready) Changed?.Invoke(this, change);
    }

    /// <summary>The vault-relative, '/'-separated path of <paramref name="fullPath"/> under
    /// <paramref name="root"/>.</summary>
    public static string ToRelative(string root, string fullPath)
        => Path.GetRelativePath(root, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    private bool IsHidden(string fullPath)
        => ToRelative(_root, fullPath).Split('/').Any(seg => seg.StartsWith('.'));

    public void Dispose()
    {
        _timer?.Dispose();
        _fsw?.Dispose();
    }
}
