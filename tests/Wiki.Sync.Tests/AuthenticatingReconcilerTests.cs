// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class AuthenticatingReconcilerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly AuthenticatingReconciler _auth =
        new(new Reconciler(new Diff3MergeAdapter()), new ChangeVerifier());

    private static AuthorizedPeer Peer(DeviceIdentity id, PeerRole role)
        => new(id.DeviceId, id.PublicKey, role, "n", "e");

    /// <summary>Serves a peer replica's live content, optionally corrupting one path (tamper sim).</summary>
    private sealed class Content(VaultReplica peer, string? tamperPath = null) : IRemoteContent
    {
        public string? Fetch(string path)
            => path == tamperPath ? "corrupted-in-transit" : peer.Read(path);
    }

    [Fact]
    public void Authorized_change_is_reconciled()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Peer(owner, PeerRole.Owner), Peer(alice, PeerRole.ReadWrite) });

        var local = new VaultReplica(owner.DeviceId);
        var remote = new VaultReplica(alice.DeviceId);
        remote.Put("New.md", "from alice");
        var signed = new ChangeSigner(alice).SignIndex(remote.Index);

        var report = _auth.Reconcile(local, peers, owner.DeviceId, alice.DeviceId, signed, new Content(remote), Now);

        Assert.Equal("from alice", local.Read("New.md"));
        Assert.Equal(1, report.Inner.Adopted);
        Assert.False(report.ListRejected);
    }

    [Fact]
    public void Unauthorized_change_is_not_applied()
    {
        using var owner = DeviceIdentity.Create();
        using var stranger = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Peer(owner, PeerRole.Owner) });

        var local = new VaultReplica(owner.DeviceId);
        var remote = new VaultReplica(stranger.DeviceId);
        remote.Put("Evil.md", "injected");
        var signed = new ChangeSigner(stranger).SignIndex(remote.Index);

        var report = _auth.Reconcile(local, peers, owner.DeviceId, stranger.DeviceId, signed, new Content(remote), Now);

        Assert.Null(local.Read("Evil.md"));
        Assert.Equal(1, report.Unauthorized);
        Assert.Equal(0, report.Inner.Adopted);
    }

    [Fact]
    public void Read_only_change_is_not_applied()
    {
        using var owner = DeviceIdentity.Create();
        using var reader = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Peer(owner, PeerRole.Owner), Peer(reader, PeerRole.ReadOnly) });

        var local = new VaultReplica(owner.DeviceId);
        var remote = new VaultReplica(reader.DeviceId);
        remote.Put("Note.md", "reader edit");
        var signed = new ChangeSigner(reader).SignIndex(remote.Index);

        var report = _auth.Reconcile(local, peers, owner.DeviceId, reader.DeviceId, signed, new Content(remote), Now);

        Assert.Null(local.Read("Note.md"));
        Assert.Equal(1, report.ReadOnlyDenied);
    }

    [Fact]
    public void Tampered_content_is_not_applied()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Peer(owner, PeerRole.Owner), Peer(alice, PeerRole.ReadWrite) });

        var local = new VaultReplica(owner.DeviceId);
        var remote = new VaultReplica(alice.DeviceId);
        remote.Put("Note.md", "authentic");
        var signed = new ChangeSigner(alice).SignIndex(remote.Index);
        // Signature is valid, but the transport delivers different bytes than the signed hash promises.
        var content = new Content(remote, tamperPath: "Note.md");

        var report = _auth.Reconcile(local, peers, owner.DeviceId, alice.DeviceId, signed, content, Now);

        Assert.Null(local.Read("Note.md"));
        Assert.Equal(1, report.Tampered);
        Assert.Equal(0, report.Inner.Adopted);
    }

    [Fact]
    public void Forged_list_rejects_everything()
    {
        using var owner = DeviceIdentity.Create();
        using var attacker = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        // A list "signed" by an attacker, not the pinned owner.
        var forged = AuthorizedPeersList.Sign(attacker, new[] { Peer(attacker, PeerRole.Owner), Peer(alice, PeerRole.ReadWrite) });

        var local = new VaultReplica(owner.DeviceId);
        var remote = new VaultReplica(alice.DeviceId);
        remote.Put("Note.md", "x");
        var signed = new ChangeSigner(alice).SignIndex(remote.Index);

        var report = _auth.Reconcile(local, forged, owner.DeviceId, alice.DeviceId, signed, new Content(remote), Now);

        Assert.True(report.ListRejected);
        Assert.Null(local.Read("Note.md"));
        Assert.Equal(0, report.Inner.Adopted);
    }
}
