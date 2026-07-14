// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class AuthorizedPeersListTests
{
    private static AuthorizedPeer Peer(DeviceIdentity id, PeerRole role, string name = "n", string email = "e")
        => new(id.DeviceId, id.PublicKey, role, name, email);

    [Fact]
    public void Signed_list_verifies_against_the_pinned_owner()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var list = AuthorizedPeersList.Sign(owner, new[]
        {
            Peer(owner, PeerRole.Owner),
            Peer(alice, PeerRole.ReadWrite),
        });
        Assert.True(list.Verify(owner.DeviceId));
    }

    [Fact]
    public void Verify_fails_for_a_different_pinned_owner()
    {
        using var owner = DeviceIdentity.Create();
        using var other = DeviceIdentity.Create();
        var list = AuthorizedPeersList.Sign(owner, new[] { Peer(owner, PeerRole.Owner) });
        Assert.False(list.Verify(other.DeviceId));
    }

    [Fact]
    public void A_list_without_an_owner_entry_fails_verification()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        // Owner signs but forgets to include itself as an Owner-role peer.
        var list = AuthorizedPeersList.Sign(owner, new[] { Peer(alice, PeerRole.ReadWrite) });
        Assert.False(list.Verify(owner.DeviceId));
    }

    [Fact]
    public void Tampering_a_peer_role_breaks_verification()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var list = AuthorizedPeersList.Sign(owner, new[]
        {
            Peer(owner, PeerRole.Owner),
            Peer(alice, PeerRole.ReadOnly),
        });
        // Forge Alice up to ReadWrite while keeping the owner's signature.
        var forged = new AuthorizedPeersList(list.OwnerDeviceId, new[]
        {
            Peer(owner, PeerRole.Owner),
            Peer(alice, PeerRole.ReadWrite),
        }, list.Signature);
        Assert.False(forged.Verify(owner.DeviceId));
    }

    [Fact]
    public void A_peer_whose_device_id_mismatches_its_key_is_rejected()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var badEntry = new AuthorizedPeer("not-alices-real-id", alice.PublicKey, PeerRole.ReadWrite, "n", "e");
        var list = AuthorizedPeersList.Sign(owner, new[] { Peer(owner, PeerRole.Owner), badEntry });
        Assert.False(list.Verify(owner.DeviceId));
    }

    [Fact]
    public void RoleOf_and_Find_return_the_recorded_entry()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var list = AuthorizedPeersList.Sign(owner, new[]
        {
            Peer(owner, PeerRole.Owner),
            Peer(alice, PeerRole.ReadOnly, "Alice", "a@x"),
        });
        Assert.Equal(PeerRole.ReadOnly, list.RoleOf(alice.DeviceId));
        Assert.Equal("Alice", list.Find(alice.DeviceId)!.Name);
        Assert.Null(list.RoleOf("nobody"));
        Assert.Null(list.Find("nobody"));
    }
}
