// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Vault;

namespace Wiki.Core.Sync;

/// <summary>The v1 sync engine: no peers, no network. Always idle; observing a change does nothing.
/// Exists so the rest of the app is built against the seam from day one; the real Wiki.Sync.P2P engine
/// (its own sub-project) drops in behind this interface without touching the editor or index.</summary>
public sealed class LocalNoOpSyncEngine : ISyncEngine
{
    public SyncStatus Status => SyncStatus.Idle;
    public IReadOnlyList<VaultChange> PendingForPeer => Array.Empty<VaultChange>();
    public void Observe(VaultChange change) { /* no peers — intentionally nothing */ }
    // No-op accessors satisfy the seam without a backing field (the no-op never raises this).
    public event EventHandler<SyncStatus>? StatusChanged { add { } remove { } }
}
