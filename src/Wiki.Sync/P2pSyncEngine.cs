// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Sync;
using Wiki.Core.Vault;

namespace Wiki.Sync;

/// <summary>The real sync engine behind the <see cref="ISyncEngine"/> seam. It maintains a
/// <see cref="VaultReplica"/> from the file-layer <see cref="VaultChange"/> events the watcher emits —
/// exactly the events the index observes, so sync stays decoupled from editor/index. Reconciliation
/// against peers runs through <see cref="Reconciler"/>; the network transport (and the
/// <see cref="PendingForPeer"/> outbound queue) arrive in a later plan.</summary>
public sealed class P2pSyncEngine(string deviceId, IContentSource content) : ISyncEngine
{
    private readonly IContentSource _content = content;

    public VaultReplica Replica { get; } = new(deviceId);

    public SyncStatus Status => SyncStatus.Idle;

    public IReadOnlyList<VaultChange> PendingForPeer => Array.Empty<VaultChange>();

    public event EventHandler<SyncStatus>? StatusChanged { add { } remove { } }

    public void Observe(VaultChange change)
    {
        switch (change.Kind)
        {
            case VaultChangeKind.Added:
            case VaultChangeKind.Modified:
                if (_content.Read(change.Path) is { } text) Replica.Put(change.Path, text);
                break;
            case VaultChangeKind.Deleted:
                Replica.Delete(change.Path);
                break;
            case VaultChangeKind.Renamed:
                if (change.OldPath is { } old) Replica.Delete(old);
                if (_content.Read(change.Path) is { } moved) Replica.Put(change.Path, moved);
                break;
        }
    }
}
