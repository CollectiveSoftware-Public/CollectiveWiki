// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Vault;

namespace Wiki.Core.Tests;

/// <summary>An in-memory <see cref="IVaultFileSystem"/> for headless tests. '/'-relative paths.</summary>
public sealed class InMemoryVaultFileSystem : IVaultFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    public InMemoryVaultFileSystem() { }
    public InMemoryVaultFileSystem(IDictionary<string, string> seed)
    {
        foreach (var kv in seed) _files[kv.Key] = kv.Value;
    }

    /// <summary>Loads every *.md under a real directory as '/'-relative entries (for the corpus fixture).</summary>
    public static InMemoryVaultFileSystem FromDirectory(string root)
    {
        var fs = new InMemoryVaultFileSystem();
        foreach (var path in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
            fs._files[Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/')] =
                File.ReadAllText(path);
        return fs;
    }

    public IReadOnlyList<string> EnumerateMarkdownFiles()
    {
        var list = _files.Keys.Where(k => k.EndsWith(".md", StringComparison.OrdinalIgnoreCase)).ToList();
        list.Sort(StringComparer.Ordinal);
        return list;
    }
    public IReadOnlyList<string> EnumerateFiles()
    {
        var list = _files.Keys.ToList();
        list.Sort(StringComparer.Ordinal);
        return list;
    }
    public string ReadAllText(string relativePath) => _files[relativePath];
    public void WriteAllText(string relativePath, string content) => _files[relativePath] = content;
    // Bytes are round-tripped through Latin1 (a lossless 1:1 byte↔char map) so the entry appears in
    // Exists/EnumerateFiles like any other file — the double never needs to interpret asset content.
    public void WriteAllBytes(string relativePath, byte[] data)
        => _files[relativePath] = System.Text.Encoding.Latin1.GetString(data);
    public void Rename(string fromRelative, string toRelative)
    {
        _files[toRelative] = _files[fromRelative];
        _files.Remove(fromRelative);
    }
    public void Delete(string relativePath) => _files.Remove(relativePath);
    public void Invalidate() { }
    public bool Exists(string relativePath) => _files.ContainsKey(relativePath);

    private readonly HashSet<string> _folders = new(StringComparer.Ordinal);

    public void CreateDirectory(string relativePath)
    {
        foreach (var f in Ancestors(relativePath.TrimEnd('/'))) _folders.Add(f);
    }

    public IReadOnlyList<string> EnumerateFolders()
    {
        var set = new HashSet<string>(_folders, StringComparer.Ordinal);
        foreach (var file in _files.Keys)
        {
            int slash = file.LastIndexOf('/');
            if (slash > 0) foreach (var f in Ancestors(file[..slash])) set.Add(f);
        }
        var list = set.Where(s => s.Length > 0).ToList();
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    public void DeleteDirectory(string relativePath)
    {
        string self = relativePath.TrimEnd('/');
        string prefix = self + "/";
        foreach (var key in _files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            _files.Remove(key);
        _folders.RemoveWhere(f => f == self || f.StartsWith(prefix, StringComparison.Ordinal));
    }

    // Each nested folder path in "a/b/c" → "a", "a/b", "a/b/c".
    private static IEnumerable<string> Ancestors(string folder)
    {
        if (string.IsNullOrEmpty(folder)) yield break;
        string acc = "";
        foreach (var part in folder.Split('/'))
        {
            acc = acc.Length == 0 ? part : acc + "/" + part;
            yield return acc;
        }
    }
}
