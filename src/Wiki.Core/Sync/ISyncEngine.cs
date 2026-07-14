// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Vault;

namespace Wiki.Core.Sync;

public enum SyncStatus { Idle, Syncing, Offline }

/// <summary>The sync seam (spec §8). Observes vault mutations and reconciles with peers; the conflict
/// strategy is a swappable object inside the real engine. v1 ships <see cref="LocalNoOpSyncEngine"/>.
/// Sync operates at the FILE layer — it never touches the editor or the index, only IVaultWatcher events.</summary>
public interface ISyncEngine
{
    SyncStatus Status { get; }
    IReadOnlyList<VaultChange> PendingForPeer { get; }
    void Observe(VaultChange change);
    event EventHandler<SyncStatus>? StatusChanged;
}
