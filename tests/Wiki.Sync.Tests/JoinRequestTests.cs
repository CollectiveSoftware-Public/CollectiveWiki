// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class JoinRequestTests
{
    private static readonly Guid Vault = new("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Expiry = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static InvitePayload Invite(string owner)
        => new(owner, Vault, PeerRole.ReadWrite, new byte[] { 9, 9, 9 }, Expiry, "");

    [Fact]
    public void A_signed_join_request_verifies()
    {
        using var owner = DeviceIdentity.Create();
        using var joiner = DeviceIdentity.Create();
        var request = JoinRequestFactory.Create(joiner, Invite(owner.DeviceId), "Joiner", "j@x");
        Assert.True(JoinRequestFactory.Verify(request));
        Assert.Equal(joiner.DeviceId, request.Applicant.DeviceId);
        Assert.Equal(Vault, request.VaultId);
    }

    [Fact]
    public void Tampering_the_presented_token_breaks_verification()
    {
        using var owner = DeviceIdentity.Create();
        using var joiner = DeviceIdentity.Create();
        var request = JoinRequestFactory.Create(joiner, Invite(owner.DeviceId), "Joiner", "j@x");
        var forged = request with { PresentedToken = new byte[] { 7, 7, 7 } };
        Assert.False(JoinRequestFactory.Verify(forged));
    }

    [Fact]
    public void Tampering_attribution_breaks_verification()
    {
        using var owner = DeviceIdentity.Create();
        using var joiner = DeviceIdentity.Create();
        var request = JoinRequestFactory.Create(joiner, Invite(owner.DeviceId), "Joiner", "j@x");
        var forged = request with { Applicant = request.Applicant with { Name = "Mallory" } };
        Assert.False(JoinRequestFactory.Verify(forged));
    }

    [Fact]
    public void A_request_whose_device_id_mismatches_its_key_is_rejected()
    {
        using var joiner = DeviceIdentity.Create();
        // Signature is valid, but the claimed device id is not the fingerprint of the key.
        var applicant = new PeerIdentity("wrong-id", joiner.PublicKey, "n", "e");
        var sig = joiner.Sign(JoinRequestFactory.Canonical(Vault, new byte[] { 9, 9, 9 }, applicant));
        var request = new JoinRequest(Vault, new byte[] { 9, 9, 9 }, applicant, sig);
        Assert.False(JoinRequestFactory.Verify(request));
    }
}
