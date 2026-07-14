// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class ChangeVerifierTests
{
    private static AuthorizedPeer Peer(DeviceIdentity id, PeerRole role)
        => new(id.DeviceId, id.PublicKey, role, "n", "e");

    private static FileEntry Entry(DeviceIdentity author, string hash = "abc")
        => new("Note.md", VersionVector.Empty.Increment(author.DeviceId), hash, false);

    private readonly ChangeVerifier _verifier = new();

    [Fact]
    public void Authorized_read_write_change_is_accepted()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Peer(owner, PeerRole.Owner), Peer(alice, PeerRole.ReadWrite) });
        var signed = new ChangeSigner(alice).Sign(Entry(alice));
        Assert.Equal(ChangeVerdict.Accepted, _verifier.Verify(peers, signed));
    }

    [Fact]
    public void Owner_change_is_accepted()
    {
        using var owner = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Peer(owner, PeerRole.Owner) });
        var signed = new ChangeSigner(owner).Sign(Entry(owner));
        Assert.Equal(ChangeVerdict.Accepted, _verifier.Verify(peers, signed));
    }

    [Fact]
    public void Unknown_signer_is_unauthorized()
    {
        using var owner = DeviceIdentity.Create();
        using var stranger = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Peer(owner, PeerRole.Owner) });
        var signed = new ChangeSigner(stranger).Sign(Entry(stranger));
        Assert.Equal(ChangeVerdict.Unauthorized, _verifier.Verify(peers, signed));
    }

    [Fact]
    public void Read_only_signer_is_denied()
    {
        using var owner = DeviceIdentity.Create();
        using var reader = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Peer(owner, PeerRole.Owner), Peer(reader, PeerRole.ReadOnly) });
        var signed = new ChangeSigner(reader).Sign(Entry(reader));
        Assert.Equal(ChangeVerdict.ReadOnlyDenied, _verifier.Verify(peers, signed));
    }

    [Fact]
    public void Tampered_metadata_is_detected()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var peers = AuthorizedPeersList.Sign(owner, new[] { Peer(owner, PeerRole.Owner), Peer(alice, PeerRole.ReadWrite) });
        var signed = new ChangeSigner(alice).Sign(Entry(alice, "abc"));
        // Keep Alice's signature but swap the entry's content hash.
        var forged = signed with { Entry = signed.Entry with { ContentHash = "deadbeef" } };
        Assert.Equal(ChangeVerdict.TamperedMetadata, _verifier.Verify(peers, forged));
    }
}
