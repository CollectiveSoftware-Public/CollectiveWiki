// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Wiki.Core.Vault;

namespace Wiki.Core.Workspace;

/// <summary>An ordered set of bookmarked note paths, persisted to <c>.cwiki/bookmarks.json</c>. This is
/// user config that travels with the vault (a <c>.cwiki/</c> sidecar config) — it is not
/// part of the rebuildable index cache, so a re-index never wipes it. Pure over <see cref="IVaultFileSystem"/>.</summary>
public sealed class BookmarkStore
{
    private const string Rel = ".cwiki/bookmarks.json";
    private readonly IVaultFileSystem _files;
    private List<string> _paths = new();

    public BookmarkStore(IVaultFileSystem files) { _files = files; Load(); }

    public IReadOnlyList<string> Paths => _paths;
    public bool IsBookmarked(string path) => _paths.Contains(path);

    public void Load() =>
        _paths = _files.Exists(Rel)
            ? (JsonSerializer.Deserialize<List<string>>(_files.ReadAllText(Rel)) ?? new())
            : new();

    public void Add(string path) { if (!_paths.Contains(path)) { _paths.Add(path); Save(); } }
    public void Remove(string path) { if (_paths.Remove(path)) Save(); }
    public void Toggle(string path) { if (!_paths.Remove(path)) _paths.Add(path); Save(); }

    /// <summary>Updates a bookmarked path in place (called on note rename/move) so the bookmark keeps
    /// pointing at the same note; preserves list order. No-op when the old path isn't bookmarked.</summary>
    public void Rename(string oldPath, string newPath)
    {
        int i = _paths.IndexOf(oldPath);
        if (i >= 0) { _paths[i] = newPath; Save(); }
    }

    private void Save()
    {
        _files.CreateDirectory(".cwiki");
        _files.WriteAllText(Rel, JsonSerializer.Serialize(_paths));
    }
}
