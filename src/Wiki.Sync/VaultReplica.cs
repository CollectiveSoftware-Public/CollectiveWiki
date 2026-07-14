// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Sync;

/// <summary>One device's view of the shared vault: per-path content + version vector + the ancestor
/// "base" (the content both peers last agreed on, used as the common ancestor for 3-way merges).
/// Local edits go through <see cref="Put"/>/<see cref="Delete"/> (which bump this device's counter);
/// the <see cref="Reconciler"/> applies remote state through <see cref="ApplyReconciled"/>.</summary>
public sealed class VaultReplica(string deviceId)
{
    private sealed class Entry
    {
        public string? Content;
        public VersionVector Version = VersionVector.Empty;
        public bool Deleted;
        public string? Base;     // last agreed-on content (ancestor); null until first sync
    }

    private readonly Dictionary<string, Entry> _entries = new();

    public string DeviceId { get; } = deviceId;

    public IEnumerable<string> Paths => _entries.Keys;

    private Entry GetOrAdd(string path)
    {
        if (!_entries.TryGetValue(path, out var e)) { e = new Entry(); _entries[path] = e; }
        return e;
    }

    /// <summary>A local create/modify: set content, clear any tombstone, bump this device's counter.
    /// The ancestor base is left untouched (it still reflects the last synced state).</summary>
    public void Put(string path, string text)
    {
        var e = GetOrAdd(path);
        e.Content = text;
        e.Deleted = false;
        e.Version = e.Version.Increment(DeviceId);
    }

    /// <summary>A local delete: tombstone the entry, drop content, bump this device's counter.</summary>
    public void Delete(string path)
    {
        var e = GetOrAdd(path);
        e.Content = null;
        e.Deleted = true;
        e.Version = e.Version.Increment(DeviceId);
    }

    public string? Read(string path) => _entries.TryGetValue(path, out var e) && !e.Deleted ? e.Content : null;

    public string? BaseOf(string path) => _entries.TryGetValue(path, out var e) ? e.Base : null;

    public FileEntry? Find(string path)
        => _entries.TryGetValue(path, out var e)
            ? new FileEntry(path, e.Version, e.Deleted ? "" : ContentHash.Of(e.Content ?? ""), e.Deleted)
            : null;

    public IReadOnlyCollection<FileEntry> Index => _entries.Keys.Select(p => Find(p)!).ToList();

    /// <summary>Apply a reconciled outcome from the <see cref="Reconciler"/>: set content/version/deleted
    /// and record the new agreed-on ancestor base (= the content, or null for a tombstone).</summary>
    public void ApplyReconciled(string path, string? content, VersionVector version, bool deleted)
    {
        var e = GetOrAdd(path);
        e.Content = deleted ? null : content;
        e.Deleted = deleted;
        e.Version = version;
        e.Base = deleted ? null : content;
    }

    /// <summary>Record the current live content as the agreed-on ancestor base — called when a reconcile
    /// finds the local and remote versions Equal (both peers now hold this exact state). A file created
    /// locally via <see cref="Put"/> has no base until this confirmation; without it the originating replica
    /// could never 3-way-merge a later concurrent edit (it would fall straight to a conflicted copy).</summary>
    public void ConfirmBase(string path)
    {
        if (_entries.TryGetValue(path, out var e))
            e.Base = e.Deleted ? null : e.Content;
    }

    /// <summary>A serializable snapshot of one entry — everything needed to restore replica state faithfully,
    /// including the ancestor base independently of the live content.</summary>
    public sealed record ReplicaEntrySnapshot(
        string Path, string? Content, IReadOnlyDictionary<string, long> Version, bool Deleted, string? Base);

    /// <summary>Capture the full per-path state (content + version vector + deleted + base) for persistence.</summary>
    public IReadOnlyList<ReplicaEntrySnapshot> Snapshot()
        => _entries.Select(kv => new ReplicaEntrySnapshot(
                kv.Key, kv.Value.Content,
                kv.Value.Version.Devices.ToDictionary(d => d, d => kv.Value.Version[d]),
                kv.Value.Deleted, kv.Value.Base))
            .ToList();

    /// <summary>Replace all state with a persisted snapshot. Restores the base independently of content
    /// (which <see cref="ApplyReconciled"/> cannot — it forces base = content).</summary>
    public void Restore(IEnumerable<ReplicaEntrySnapshot> snapshots)
    {
        _entries.Clear();
        foreach (var s in snapshots)
            _entries[s.Path] = new Entry
            {
                Content = s.Content,
                Version = new VersionVector(s.Version),
                Deleted = s.Deleted,
                Base = s.Base,
            };
    }
}
