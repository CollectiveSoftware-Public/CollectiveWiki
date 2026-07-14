// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;
using static Wiki.Sync.Tests.AuthenticatedSyncSimulator;

namespace Wiki.Sync.Tests;

public class AuthenticatedConvergenceGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private static AuthorizedPeer Entry(DeviceIdentity id, PeerRole role)
        => new(id.DeviceId, id.PublicKey, role, "n", "e");

    private static IRemoteContent Live(VaultReplica peer) => new LiveAdapter(peer);
    private sealed class LiveAdapter(VaultReplica peer) : IRemoteContent
    {
        public string? Fetch(string path) => peer.Read(path);
    }

    [Fact]
    public void Two_authorized_peers_converge()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Entry(owner, PeerRole.Owner), Entry(alice, PeerRole.ReadWrite) });

        var o = NewPeer(owner);
        var a = NewPeer(alice);
        o.Replica.Put("Owner.md", "owned");
        a.Replica.Put("Alice.md", "aliced");

        RunToFixpoint(o, a, peers, owner.DeviceId, Now);

        Assert.Equal("owned", a.Replica.Read("Owner.md"));
        Assert.Equal("aliced", o.Replica.Read("Alice.md"));
    }

    [Fact]
    public void Concurrent_authorized_edits_merge()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Entry(owner, PeerRole.Owner), Entry(alice, PeerRole.ReadWrite) });

        var o = NewPeer(owner);
        var a = NewPeer(alice);
        // Shared common ancestor on both peers.
        var baseVec = VersionVector.Empty.Increment("seed");
        o.Replica.ApplyReconciled("Note.md", "one\ntwo\nthree", baseVec, false);
        a.Replica.ApplyReconciled("Note.md", "one\ntwo\nthree", baseVec, false);
        // Disjoint concurrent edits.
        o.Replica.Put("Note.md", "ONE\ntwo\nthree");
        a.Replica.Put("Note.md", "one\ntwo\nTHREE");

        RunToFixpoint(o, a, peers, owner.DeviceId, Now);

        Assert.Equal("ONE\ntwo\nTHREE", o.Replica.Read("Note.md"));
        Assert.Equal(o.Replica.Read("Note.md"), a.Replica.Read("Note.md"));
    }

    // Not a RunToFixpoint scenario: the reader's rejected file lives only on the reader by design, so a
    // union-convergence check would (correctly) see divergence there. Drive the two directions manually
    // and assert the two facts. Reconcile OWNER-from-reader FIRST — before the reader adopts the owner's
    // file — so the reader's index is just {Sneaky.md} and the ReadOnlyDenied count is exactly 1.
    [Fact]
    public void Read_only_peer_receives_but_cannot_push()
    {
        using var owner = DeviceIdentity.Create();
        using var reader = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Entry(owner, PeerRole.Owner), Entry(reader, PeerRole.ReadOnly) });

        var o = NewPeer(owner);
        var r = NewPeer(reader);
        o.Replica.Put("Shared.md", "from owner");
        r.Replica.Put("Sneaky.md", "reader tried to add this");

        var rec = new AuthenticatingReconciler(new Reconciler(new Diff3MergeAdapter()), new ChangeVerifier());
        var toOwner = rec.Reconcile(o.Replica, peers, owner.DeviceId, reader.DeviceId,
            r.Signer.SignIndex(r.Replica.Index), Live(r.Replica), Now);
        var toReader = rec.Reconcile(r.Replica, peers, owner.DeviceId, owner.DeviceId,
            o.Signer.SignIndex(o.Replica.Index), Live(o.Replica), Now);

        Assert.Null(o.Replica.Read("Sneaky.md"));                 // reader's push was ignored
        Assert.Equal(1, toOwner.ReadOnlyDenied);
        Assert.Equal("from owner", r.Replica.Read("Shared.md"));  // owner's change reached the reader
        Assert.Equal(1, toReader.Inner.Adopted);
    }
}
