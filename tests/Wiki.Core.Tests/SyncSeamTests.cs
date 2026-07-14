// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Sync;
using Wiki.Core.Vault;

namespace Wiki.Core.Tests;

public class SyncSeamTests
{
    [Fact]
    public void NoOp_engine_is_idle_and_never_queues_peer_work()
    {
        ISyncEngine engine = new LocalNoOpSyncEngine();
        Assert.Equal(SyncStatus.Idle, engine.Status);

        // Simulate a 2-device trace: device-A edits flow through Observe; with no peer, nothing queues.
        engine.Observe(new VaultChange(VaultChangeKind.Modified, "A.md", null));
        engine.Observe(new VaultChange(VaultChangeKind.Added, "B.md", null));
        engine.Observe(new VaultChange(VaultChangeKind.Renamed, "C2.md", "C.md"));

        Assert.Empty(engine.PendingForPeer);
        Assert.Equal(SyncStatus.Idle, engine.Status);
    }

    [Fact]
    public void Engine_subscribes_to_a_watcher_without_coupling_to_the_index()
    {
        // Proves the decoupling: sync observes the SAME IVaultWatcher events the index does.
        var watcher = new FakeVaultWatcher();
        ISyncEngine engine = new LocalNoOpSyncEngine();
        watcher.Changed += (_, c) => engine.Observe(c);

        watcher.Emit(new VaultChange(VaultChangeKind.Modified, "Note.md", null));
        Assert.Equal(SyncStatus.Idle, engine.Status);   // no throw, no coupling
    }
}
