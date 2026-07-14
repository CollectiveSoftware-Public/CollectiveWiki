// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Vault;
using Wiki.Sync;

namespace Wiki.Sync.Host;

/// <summary>Bridges a <see cref="VaultReplica"/> to the vault's .md files: seeds the replica from the notes
/// on disk (first share/join) and flushes reconciled results back (create/overwrite live notes, delete
/// tombstoned ones, land conflicted copies as their own notes). Offline-edit detection (files changed while
/// the app was closed) is a follow-up; the head keeps the replica current via the file watcher.</summary>
public sealed class VaultReplicaBridge(IVaultFileSystem vault)
{
    private readonly IVaultFileSystem _vault = vault;

    /// <summary>Put every note the replica does not yet know — the initial local state on first share/join.</summary>
    public void SeedFromVault(VaultReplica replica)
    {
        foreach (var path in _vault.EnumerateMarkdownFiles())
            if (replica.Find(path) is null)
                replica.Put(path, _vault.ReadAllText(path));
    }

    /// <summary>Reconcile the replica with the current on-disk notes: Put any note whose content differs from
    /// (or is unknown/tombstoned in) the replica, and tombstone any live note that has vanished from disk. Each
    /// change bumps this device's counter so it propagates on the next sync. Catches edits made through the app
    /// AND while it was closed (offline-edit detection). Returns the paths whose replica state changed.</summary>
    public IReadOnlyList<string> RefreshFromVault(VaultReplica replica)
    {
        _vault.Invalidate();   // observe notes changed out-of-band (a different fs instance, or edits while closed)
        var changed = new List<string>();
        var onDisk = new HashSet<string>(_vault.EnumerateMarkdownFiles());
        foreach (var path in onDisk)
        {
            var content = _vault.ReadAllText(path);
            if (replica.Read(path) != content)   // null (unknown/tombstoned) or different → a local change
            {
                replica.Put(path, content);
                changed.Add(path);
            }
        }
        foreach (var path in replica.Paths.ToList())
            if (replica.Find(path) is { Deleted: false } && !onDisk.Contains(path))
            {
                replica.Delete(path);
                changed.Add(path);
            }
        return changed;
    }

    /// <summary>Write the replica's live state to disk: overwrite changed/new notes, delete tombstones.
    /// Returns the paths whose file content changed (so the head can refresh the tree / open tabs).</summary>
    public IReadOnlyList<string> FlushToVault(VaultReplica replica)
    {
        var changed = new List<string>();
        foreach (var path in replica.Paths.ToList())
        {
            var entry = replica.Find(path)!;
            if (entry.Deleted)
            {
                if (_vault.Exists(path)) { _vault.Delete(path); changed.Add(path); }
            }
            else
            {
                var content = replica.Read(path) ?? "";
                if (!_vault.Exists(path) || _vault.ReadAllText(path) != content)
                {
                    _vault.WriteAllText(path, content);
                    changed.Add(path);
                }
            }
        }
        return changed;
    }
}
