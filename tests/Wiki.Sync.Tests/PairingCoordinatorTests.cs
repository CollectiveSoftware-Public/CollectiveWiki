// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class PairingCoordinatorTests
{
    private static readonly Guid Vault = new("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Expiry = Now.AddHours(1);

    [Fact]
    public void New_coordinator_lists_only_the_owner()
    {
        using var owner = DeviceIdentity.Create();
        var c = new PairingCoordinator(owner, Vault, "Owner", "o@x");
        Assert.Equal(PeerRole.Owner, c.CurrentList().RoleOf(owner.DeviceId));
        Assert.Single(c.Peers);
    }

    [Fact]
    public void Issue_returns_a_role_tagged_invite_for_this_vault()
    {
        using var owner = DeviceIdentity.Create();
        var c = new PairingCoordinator(owner, Vault, "Owner", "o@x");
        var invite = c.Issue(PeerRole.ReadOnly, Expiry, "hint");
        Assert.Equal(owner.DeviceId, invite.OwnerDeviceId);
        Assert.Equal(Vault, invite.VaultId);
        Assert.Equal(PeerRole.ReadOnly, invite.Role);
        Assert.Equal(32, invite.PairingToken.Length);
        Assert.Equal("hint", invite.DiscoveryHint);
    }

    [Fact]
    public void Accepting_a_valid_request_records_the_peer_at_the_granted_role()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var c = new PairingCoordinator(owner, Vault, "Owner", "o@x");
        var request = JoinRequestFactory.Create(alice, c.Issue(PeerRole.ReadOnly, Expiry), "Alice", "a@x");

        var result = c.Accept(request, Now);

        Assert.Equal(PairingOutcome.Accepted, result.Outcome);
        Assert.Equal(PeerRole.ReadOnly, result.UpdatedList!.RoleOf(alice.DeviceId));   // granted role, not self-asserted
        Assert.Equal("Alice", result.UpdatedList.Find(alice.DeviceId)!.Name);
        Assert.True(result.UpdatedList.Verify(owner.DeviceId));                         // still a valid owner-signed list
    }

    [Fact]
    public void An_unknown_token_is_rejected()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var c = new PairingCoordinator(owner, Vault, "Owner", "o@x");
        var stray = new InvitePayload(owner.DeviceId, Vault, PeerRole.ReadWrite, new byte[] { 1, 2, 3 }, Expiry, "");
        var request = JoinRequestFactory.Create(alice, stray, "Alice", "a@x");
        Assert.Equal(PairingOutcome.UnknownToken, c.Accept(request, Now).Outcome);
    }

    [Fact]
    public void An_expired_invite_is_rejected()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var c = new PairingCoordinator(owner, Vault, "Owner", "o@x");
        var request = JoinRequestFactory.Create(alice, c.Issue(PeerRole.ReadWrite, Expiry), "Alice", "a@x");
        Assert.Equal(PairingOutcome.Expired, c.Accept(request, Expiry.AddSeconds(1)).Outcome);
    }

    [Fact]
    public void A_token_is_single_use()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var c = new PairingCoordinator(owner, Vault, "Owner", "o@x");
        var request = JoinRequestFactory.Create(alice, c.Issue(PeerRole.ReadWrite, Expiry), "Alice", "a@x");
        Assert.Equal(PairingOutcome.Accepted, c.Accept(request, Now).Outcome);
        Assert.Equal(PairingOutcome.AlreadyUsed, c.Accept(request, Now).Outcome);
    }

    [Fact]
    public void A_request_for_a_different_vault_is_rejected()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var c = new PairingCoordinator(owner, Vault, "Owner", "o@x");
        var invite = c.Issue(PeerRole.ReadWrite, Expiry) with { VaultId = Guid.NewGuid() };
        var request = JoinRequestFactory.Create(alice, invite, "Alice", "a@x");
        Assert.Equal(PairingOutcome.WrongVault, c.Accept(request, Now).Outcome);
    }

    [Fact]
    public void A_tampered_request_is_rejected_as_invalid_signature()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var c = new PairingCoordinator(owner, Vault, "Owner", "o@x");
        var request = JoinRequestFactory.Create(alice, c.Issue(PeerRole.ReadWrite, Expiry), "Alice", "a@x");
        var forged = request with { Applicant = request.Applicant with { Name = "Mallory" } };
        Assert.Equal(PairingOutcome.InvalidSignature, c.Accept(forged, Now).Outcome);
    }

    [Fact]
    public void A_request_whose_device_id_mismatches_its_key_is_identity_mismatch()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var c = new PairingCoordinator(owner, Vault, "Owner", "o@x");
        var invite = c.Issue(PeerRole.ReadWrite, Expiry);
        var applicant = new PeerIdentity("wrong-id", alice.PublicKey, "Alice", "a@x");
        var sig = alice.Sign(JoinRequestFactory.Canonical(invite.VaultId, invite.PairingToken, applicant));
        var request = new JoinRequest(invite.VaultId, invite.PairingToken, applicant, sig);
        Assert.Equal(PairingOutcome.IdentityMismatch, c.Accept(request, Now).Outcome);
    }

    [Fact]
    public void Revoking_a_peer_removes_it_from_the_signed_list()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var c = new PairingCoordinator(owner, Vault, "Owner", "o@x");
        c.Accept(JoinRequestFactory.Create(alice, c.Issue(PeerRole.ReadWrite, Expiry), "Alice", "a@x"), Now);

        var afterRevoke = c.Revoke(alice.DeviceId);

        Assert.Null(afterRevoke.RoleOf(alice.DeviceId));
        Assert.True(afterRevoke.Verify(owner.DeviceId));
    }

    [Fact]
    public void The_owner_cannot_be_revoked()
    {
        using var owner = DeviceIdentity.Create();
        var c = new PairingCoordinator(owner, Vault, "Owner", "o@x");
        Assert.Equal(PeerRole.Owner, c.Revoke(owner.DeviceId).RoleOf(owner.DeviceId));
    }
}
