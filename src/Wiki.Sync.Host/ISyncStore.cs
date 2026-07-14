// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Sync.Host;

/// <summary>Named byte storage for a vault's `.cwiki/sync/` sidecar (device identity, roster, key ring,
/// replica state). A separate seam because <c>IVaultFileSystem</c> skips dot-folders, so the sidecar is not
/// reachable through it. The in-memory fake in tests implements the same contract.</summary>
public interface ISyncStore
{
    bool Exists(string name);
    byte[]? ReadBytes(string name);
    void WriteBytes(string name, byte[] data);
    void Delete(string name);
}

/// <summary>The real store: files directly under a `.cwiki/sync/` directory (created on demand).</summary>
public sealed class FileSyncStore : ISyncStore
{
    private readonly string _dir;

    public FileSyncStore(string syncDir)
    {
        _dir = syncDir;
        Directory.CreateDirectory(_dir);
    }

    private string PathOf(string name) => System.IO.Path.Combine(_dir, name);

    public bool Exists(string name) => File.Exists(PathOf(name));
    public byte[]? ReadBytes(string name) => File.Exists(PathOf(name)) ? File.ReadAllBytes(PathOf(name)) : null;
    public void WriteBytes(string name, byte[] data) => File.WriteAllBytes(PathOf(name), data);
    public void Delete(string name) { if (File.Exists(PathOf(name))) File.Delete(PathOf(name)); }
}
