// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using Collective.Platform.Secrets;
using Wiki.Sync;
using Wiki.Sync.Transport;

namespace Wiki.Sync.Host.Tests;

public class SyncHostGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static AuthenticatingReconciler NewReconciler() => new(new Reconciler(new Diff3MergeAdapter()), new ChangeVerifier());

    [Fact]
    public async Task Owner_and_joiner_sync_over_loopback_then_persist_reload_and_reconverge()
    {
        // Owner shares a seeded vault.
        var ownerVault = new FakeVault(new() { ["Shared.md"] = "the shared note" });
        var ownerStore = new InMemorySyncStore();
        var owner = VaultSyncHost.Open(ownerVault, ownerStore, new FakeSecretStore(), new ContentKeySealer(), NewReconciler());
        owner.ShareVault("Ada", "ada@x");
        var invite = owner.AddCollaborator(PeerRole.ReadWrite, Now.AddHours(1));

        // Joiner pairs (invite → join request → accept → adopt signed roster, exchanged in-process).
        var joinerVault = new FakeVault();
        var joinerStore = new InMemorySyncStore();
        ISecretStore joinerSecrets = new FakeSecretStore();
        var joiner = VaultSyncHost.Open(joinerVault, joinerStore, joinerSecrets, new ContentKeySealer(), NewReconciler());
        Assert.Equal(PairingOutcome.Accepted, owner.AcceptJoin(joiner.CreateJoinRequest(invite, "Bob", "bob@x")!, Now));
        Assert.True(joiner.AdoptRoster(owner.Peers!));

        // Owner serves on loopback; joiner dials + pulls over real mutual TLS.
        using var listener = new SyncListener(owner.Identity, owner.BuildSyncServer(), owner.AcceptPeer);
        var ep = listener.Start(new IPEndPoint(IPAddress.Loopback, 0));

        using (var conn = await PeerConnector.ConnectAsync(joiner.Identity, owner.DeviceId, ep, relay: null))
        {
            var report = await joiner.PullFromAsync(conn, Now);
            Assert.Equal(1, report.Inner.Adopted);
        }
        Assert.Equal("the shared note", joinerVault.ReadAllText("Shared.md")); // synced to the joiner's disk

        // Reopen the joiner purely from its persisted `.cwiki/sync/` state; a second pull is a stable no-op.
        var joiner2 = VaultSyncHost.Open(joinerVault, joinerStore, joinerSecrets, new ContentKeySealer(), NewReconciler());
        Assert.Equal(joiner.DeviceId, joiner2.DeviceId);          // identity survived (sealed at rest)
        Assert.True(joiner2.Peers!.Verify(owner.DeviceId));       // roster survived + still verifies

        using (var conn2 = await PeerConnector.ConnectAsync(joiner2.Identity, owner.DeviceId, ep, relay: null))
        {
            var report = await joiner2.PullFromAsync(conn2, Now);
            Assert.Equal(0, report.Inner.Adopted);               // already converged — replica state survived
        }
    }
}
