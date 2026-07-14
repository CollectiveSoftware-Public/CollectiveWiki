// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Wiki.Sync;

namespace Wiki.Sync.Host;

/// <summary>Persists a <see cref="VaultReplica"/>'s per-file version-vector state (+ base ancestors) to
/// `.cwiki/sync/state.json` so convergence survives a restart. Not sealed — it holds no secret, only vector
/// metadata and note content the vault already stores in plaintext.</summary>
public sealed class ReplicaStateStore(ISyncStore store, string name = "state.json")
{
    private sealed record EntryDto(string Path, string? Content, Dictionary<string, long> Version, bool Deleted, string? Base);

    public void Save(VaultReplica replica)
    {
        var dtos = replica.Snapshot()
            .Select(s => new EntryDto(s.Path, s.Content, new Dictionary<string, long>(s.Version), s.Deleted, s.Base))
            .ToList();
        store.WriteBytes(name, JsonSerializer.SerializeToUtf8Bytes(dtos));
    }

    public void Load(VaultReplica replica)
    {
        var blob = store.ReadBytes(name);
        if (blob is null) return;
        var dtos = JsonSerializer.Deserialize<List<EntryDto>>(blob) ?? [];
        replica.Restore(dtos.Select(d =>
            new VaultReplica.ReplicaEntrySnapshot(d.Path, d.Content, d.Version, d.Deleted, d.Base)));
    }
}
