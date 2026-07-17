// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;

namespace Wiki.Sync;

/// <summary>The result of validating a <see cref="JoinRequest"/>.</summary>
public enum PairingOutcome { Accepted, UnknownToken, Expired, AlreadyUsed, WrongVault, InvalidSignature, IdentityMismatch, NoRoute, OwnerUnreachable }

/// <summary>The outcome of <see cref="PairingCoordinator.Accept"/>: the verdict, plus the reissued
/// owner-signed list when a peer was added (null otherwise).</summary>
public sealed record PairingResult(PairingOutcome Outcome, AuthorizedPeersList? UpdatedList);

/// <summary>Owner-side membership lifecycle (spec §7): issues role-tagged single-use invites, accepts
/// signed join requests (recording the collaborator at the granted role into the owner-signed
/// authorized-peers list), and revokes. Stateful and identity-bearing, so it is constructed by the head
/// (not registered in DI). Post-revocation content-key rotation is done by the head via
/// <see cref="VaultKeyRing"/> after <see cref="Revoke"/> — see the lifecycle gate.</summary>
public sealed class PairingCoordinator
{
    private sealed record PendingInvite(byte[] Token, PeerRole Role, DateTimeOffset ExpiresAt);

    private readonly DeviceIdentity _owner;
    private readonly List<AuthorizedPeer> _roster = new();
    private readonly List<PendingInvite> _pending = new();
    private readonly List<byte[]> _used = new();

    public Guid VaultId { get; }

    public PairingCoordinator(DeviceIdentity owner, Guid vaultId, string ownerName = "", string ownerEmail = "",
        IEnumerable<AuthorizedPeer>? initialRoster = null)
    {
        _owner = owner;
        VaultId = vaultId;
        _roster.Add(new AuthorizedPeer(owner.DeviceId, owner.PublicKey, PeerRole.Owner, ownerName, ownerEmail));
        if (initialRoster is not null)
            foreach (var p in initialRoster)
                if (p.DeviceId != owner.DeviceId)   // owner already seeded above
                    _roster.Add(p);
    }

    public IReadOnlyList<AuthorizedPeer> Peers => _roster;

    /// <summary>The current roster as a fresh owner-signed list (a new signature each call — ECDSA is
    /// non-deterministic, so callers must never compare two signatures byte-for-byte).</summary>
    public AuthorizedPeersList CurrentList() => AuthorizedPeersList.Sign(_owner, _roster.ToList());

    public InvitePayload Issue(PeerRole role, DateTimeOffset expiresAt, string discoveryHint = "")
    {
        var token = RandomNumberGenerator.GetBytes(32);
        _pending.Add(new PendingInvite(token, role, expiresAt));
        return new InvitePayload(_owner.DeviceId, VaultId, role, token, expiresAt, discoveryHint);
    }

    public PairingResult Accept(JoinRequest request, DateTimeOffset now)
    {
        if (request.VaultId != VaultId) return new PairingResult(PairingOutcome.WrongVault, null);
        if (Contains(_used, request.PresentedToken)) return new PairingResult(PairingOutcome.AlreadyUsed, null);

        var pending = _pending.FirstOrDefault(
            p => CryptographicOperations.FixedTimeEquals(p.Token, request.PresentedToken));
        if (pending is null) return new PairingResult(PairingOutcome.UnknownToken, null);
        if (now > pending.ExpiresAt) return new PairingResult(PairingOutcome.Expired, null);

        var applicant = request.Applicant;
        if (applicant.DeviceId != Base32.Encode(SHA256.HashData(applicant.PublicKey)))
            return new PairingResult(PairingOutcome.IdentityMismatch, null);
        if (!DeviceIdentity.Verify(applicant.PublicKey,
                JoinRequestFactory.Canonical(request.VaultId, request.PresentedToken, applicant), request.Signature))
            return new PairingResult(PairingOutcome.InvalidSignature, null);

        // Consume the token (single-use) and upsert the applicant at the OWNER-GRANTED role.
        _pending.Remove(pending);
        _used.Add(pending.Token);
        _roster.RemoveAll(p => p.DeviceId == applicant.DeviceId && p.Role != PeerRole.Owner);
        _roster.Add(new AuthorizedPeer(applicant.DeviceId, applicant.PublicKey, pending.Role, applicant.Name, applicant.Email));
        return new PairingResult(PairingOutcome.Accepted, CurrentList());
    }

    /// <summary>Drop a device from the roster and reissue the signed list. The owner cannot be revoked.
    /// For post-revocation secrecy the head also rotates the vault content key (<see cref="VaultKeyRing"/>).</summary>
    public AuthorizedPeersList Revoke(string deviceId)
    {
        _roster.RemoveAll(p => p.DeviceId == deviceId && p.Role != PeerRole.Owner);
        return CurrentList();
    }

    private static bool Contains(List<byte[]> tokens, byte[] token)
        => tokens.Any(t => CryptographicOperations.FixedTimeEquals(t, token));
}
