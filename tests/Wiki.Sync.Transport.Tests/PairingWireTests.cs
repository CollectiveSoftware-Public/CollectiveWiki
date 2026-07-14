// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Transport.Tests;

public class PairingWireTests
{
    [Fact]
    public void JoinRequest_round_trips()
    {
        var req = new JoinRequest(Guid.NewGuid(), new byte[] { 1, 2, 3 },
            new PeerIdentity("dev-abc", new byte[] { 9, 8, 7 }, "Bob", "bob@x"), new byte[] { 4, 5, 6 });

        var back = PairingWire.DecodeJoinRequest(PairingWire.EncodeJoinRequest(req));

        Assert.Equal(req.VaultId, back.VaultId);
        Assert.Equal(req.PresentedToken, back.PresentedToken);
        Assert.Equal(req.Applicant.DeviceId, back.Applicant.DeviceId);
        Assert.Equal(req.Applicant.PublicKey, back.Applicant.PublicKey);
        Assert.Equal(req.Applicant.Name, back.Applicant.Name);
        Assert.Equal(req.Applicant.Email, back.Applicant.Email);
        Assert.Equal(req.Signature, back.Signature);
    }

    [Fact]
    public void Accepted_round_trips_the_signed_roster_and_the_sealed_key()
    {
        using var owner = DeviceIdentity.Create();
        var roster = AuthorizedPeersList.Sign(owner, new[]
        {
            new AuthorizedPeer(owner.DeviceId, owner.PublicKey, PeerRole.Owner, "O", "o@x"),
        });
        var key = new SealedContentKey(3, "dev-r", new byte[] { 1 }, new byte[] { 2, 2 }, new byte[] { 3, 3, 3 });

        var (roster2, key2) = PairingWire.DecodeAccepted(PairingWire.EncodeAccepted(roster, key));

        Assert.True(roster2.Verify(owner.DeviceId));       // signature survived the round trip
        Assert.Equal(key.Epoch, key2.Epoch);
        Assert.Equal(key.RecipientDeviceId, key2.RecipientDeviceId);
        Assert.Equal(key.Ciphertext, key2.Ciphertext);
        Assert.Equal(key.Tag, key2.Tag);
    }

    [Fact]
    public void Rejected_round_trips_the_outcome()
        => Assert.Equal(PairingOutcome.Expired,
            PairingWire.DecodeRejected(PairingWire.EncodeRejected(PairingOutcome.Expired)));

    [Fact]
    public async Task A_frame_is_a_type_byte_then_a_big_endian_length_then_the_payload()
    {
        using var ms = new MemoryStream();
        await PairingWire.WriteFrameAsync(ms, PairingWire.MessageType.JoinRequest, new byte[] { 0xAA, 0xBB }, default);

        var bytes = ms.ToArray();
        Assert.Equal((byte)1, bytes[0]);                       // JoinRequest
        Assert.Equal(new byte[] { 0, 0, 0, 2 }, bytes[1..5]);  // length 2, big-endian
        Assert.Equal(new byte[] { 0xAA, 0xBB }, bytes[5..]);
    }
}
