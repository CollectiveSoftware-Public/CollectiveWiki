// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Vault;

/// <summary>An <see cref="IVaultFileSystem"/> over a real folder tree (System.IO), recursive, skipping
/// reparse points (symlink/junction loops). BCL only — safe in Core. '/'-separated relative paths.</summary>
public sealed class PhysicalVaultFileSystem : IVaultFileSystem
{
    private readonly string _root;
    private readonly object _gate = new();
    private IReadOnlyList<string>? _allCache;   // every file; memoized, invalidated on add/rename
    private IReadOnlyList<string>? _mdCache;    // notes only, derived from _allCache

    public PhysicalVaultFileSystem(string vaultRoot) => _root = Path.GetFullPath(vaultRoot);

    // Memoized: a large vault is walked once, not re-walked on every call. Link resolution calls this
    // once per link (via BacklinksOf), so re-walking disk each time froze the UI on big vaults.
    public IReadOnlyList<string> EnumerateMarkdownFiles()
    {
        var cached = _mdCache;
        if (cached is not null) return cached;
        lock (_gate)
        {
            if (_mdCache is not null) return _mdCache;
            return _mdCache = EnumerateFiles()
                .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    // All files (notes + assets), single disk walk. Image-embed resolution searches this by basename.
    public IReadOnlyList<string> EnumerateFiles()
    {
        var cached = _allCache;
        if (cached is not null) return cached;
        lock (_gate)
        {
            if (_allCache is not null) return _allCache;
            var results = new List<string>();
            Walk(_root, results);
            results.Sort(StringComparer.Ordinal);
            return _allCache = results;
        }
    }

    public void Invalidate() => InvalidateList();

    private void InvalidateList() { lock (_gate) { _allCache = null; _mdCache = null; } }

    private void Walk(string dir, List<string> results)
    {
        var info = new DirectoryInfo(dir);
        if (!info.Exists) return;
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) return;
        foreach (var file in info.EnumerateFiles())
            results.Add(Path.GetRelativePath(_root, file.FullName).Replace(Path.DirectorySeparatorChar, '/'));
        foreach (var sub in info.EnumerateDirectories())
        {
            if (sub.Name.StartsWith('.')) continue;   // skip .cwiki/.git/.obsidian/etc — never notes/assets
            Walk(sub.FullName, results);
        }
    }

    // Resolve a vault-relative path to an absolute one, REFUSING anything that escapes the vault root
    // (a '..' segment or a rooted/absolute path). This is the single choke point for every read/write —
    // notably the P2P sync flush, which writes peer-supplied paths: a paired peer must not be able to
    // write (or have us read) a file outside the vault.
    private string Full(string relativePath)
    {
        string full = Path.GetFullPath(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (full != _root && !full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException($"Path escapes the vault root: {relativePath}");
        return full;
    }

    public string ReadAllText(string relativePath) => File.ReadAllText(Full(relativePath));

    public void WriteAllText(string relativePath, string content)
    {
        string full = Full(relativePath);
        bool isNew = !File.Exists(full);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        if (isNew) InvalidateList();   // a new file changes the enumeration; an overwrite does not
    }

    public void WriteAllBytes(string relativePath, byte[] data)
    {
        string full = Full(relativePath);
        bool isNew = !File.Exists(full);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, data);
        if (isNew) InvalidateList();
    }

    public void Rename(string fromRelative, string toRelative)
    {
        string to = Full(toRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Move(Full(fromRelative), to, overwrite: false);
        InvalidateList();
    }

    public void Delete(string relativePath)
    {
        string full = Full(relativePath);
        if (File.Exists(full)) { File.Delete(full); InvalidateList(); }
    }

    public void CreateDirectory(string relativePath)
    {
        Directory.CreateDirectory(Full(relativePath));
        InvalidateList();
    }

    public IReadOnlyList<string> EnumerateFolders()
    {
        var dirs = new List<string>();
        WalkDirs(_root, dirs);
        dirs.Sort(StringComparer.Ordinal);
        return dirs;
    }

    private void WalkDirs(string dir, List<string> outp)
    {
        var info = new DirectoryInfo(dir);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0) return;
        foreach (var sub in info.EnumerateDirectories())
        {
            if (sub.Name.StartsWith('.')) continue;   // skip .cwiki/.git/.obsidian/etc
            outp.Add(Path.GetRelativePath(_root, sub.FullName).Replace(Path.DirectorySeparatorChar, '/'));
            WalkDirs(sub.FullName, outp);
        }
    }

    public void DeleteDirectory(string relativePath)
    {
        string full = Full(relativePath);
        if (Directory.Exists(full)) { Directory.Delete(full, recursive: true); InvalidateList(); }
    }

    // A path that escapes the vault (Full throws) is treated as "not a vault file" rather than an error,
    // so callers that probe a note-supplied target (e.g. asset resolution) degrade gracefully; mutating
    // operations still fail closed because they call Full directly.
    public bool Exists(string relativePath)
    {
        try { return File.Exists(Full(relativePath)); }
        catch (UnauthorizedAccessException) { return false; }
    }
}
