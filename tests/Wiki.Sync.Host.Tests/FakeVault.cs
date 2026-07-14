// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Vault;

namespace Wiki.Sync.Host.Tests;

internal sealed class FakeVault(Dictionary<string, string>? seed = null) : IVaultFileSystem
{
    private readonly Dictionary<string, string> _files = seed ?? new(StringComparer.Ordinal);

    public IReadOnlyList<string> EnumerateMarkdownFiles()
        => _files.Keys.Where(k => k.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
    public IReadOnlyList<string> EnumerateFiles() => _files.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
    public string ReadAllText(string relativePath) => _files[relativePath];
    public void WriteAllText(string relativePath, string content) => _files[relativePath] = content;
    public void WriteAllBytes(string relativePath, byte[] data) => _files[relativePath] = System.Text.Encoding.Latin1.GetString(data);
    public void Rename(string fromRelative, string toRelative) { _files[toRelative] = _files[fromRelative]; _files.Remove(fromRelative); }
    public void Delete(string relativePath) => _files.Remove(relativePath);
    public void Invalidate() { }
    public bool Exists(string relativePath) => _files.ContainsKey(relativePath);

    private readonly HashSet<string> _folders = new(StringComparer.Ordinal);
    public void CreateDirectory(string relativePath) => _folders.Add(relativePath.TrimEnd('/'));
    public IReadOnlyList<string> EnumerateFolders()
    {
        var set = new HashSet<string>(_folders, StringComparer.Ordinal);
        foreach (var file in _files.Keys)
        {
            int slash = file.LastIndexOf('/');
            if (slash > 0) set.Add(file[..slash]);
        }
        return set.Where(s => s.Length > 0).OrderBy(s => s, StringComparer.Ordinal).ToList();
    }
    public void DeleteDirectory(string relativePath)
    {
        string prefix = relativePath.TrimEnd('/') + "/";
        foreach (var key in _files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            _files.Remove(key);
        _folders.RemoveWhere(f => f == relativePath.TrimEnd('/') || f.StartsWith(prefix, StringComparison.Ordinal));
    }
}
