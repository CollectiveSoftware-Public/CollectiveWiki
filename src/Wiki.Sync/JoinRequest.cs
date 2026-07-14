// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;

namespace Wiki.Sync;

/// <summary>Who a joiner is: its pinned device id, public key (to verify its signature), and git-style
/// attribution. Deliberately has no role — a collaborator does not assign its own role; the owner grants
/// one from the pending invite (see <see cref="PairingCoordinator"/>).</summary>
public sealed record PeerIdentity(string DeviceId, byte[] PublicKey, string Name, string Email);

/// <summary>A collaborator's answer to an invite: it presents the invite's one-time token and its own
/// identity, signed with its device key. The signature binds the joiner's key to the token so a stolen
/// token cannot be used to register a different key.</summary>
public sealed record JoinRequest(Guid VaultId, byte[] PresentedToken, PeerIdentity Applicant, byte[] Signature);

/// <summary>Builds and verifies <see cref="JoinRequest"/>s.</summary>
public static class JoinRequestFactory
{
    /// <summary>Deterministic bytes both sides sign/verify: version tag, vault, token, applicant identity.
    /// Free-text name/email are length-prefixed so distinct tuples can't collide into one signable string.</summary>
    public static byte[] Canonical(Guid vaultId, byte[] presentedToken, PeerIdentity applicant)
    {
        var sb = new StringBuilder();
        sb.Append("cwikijoin/v1\n")
          .Append(vaultId.ToString("N")).Append('\n')
          .Append(Convert.ToHexString(presentedToken)).Append('\n')
          .Append(applicant.DeviceId).Append('\n')
          .Append(Convert.ToHexString(applicant.PublicKey)).Append('\n')
          .Append(applicant.Name.Length).Append(':').Append(applicant.Name).Append('\t')
          .Append(applicant.Email.Length).Append(':').Append(applicant.Email);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static JoinRequest Create(DeviceIdentity joiner, InvitePayload invite, string name, string email)
    {
        var applicant = new PeerIdentity(joiner.DeviceId, joiner.PublicKey, name, email);
        var signature = joiner.Sign(Canonical(invite.VaultId, invite.PairingToken, applicant));
        return new JoinRequest(invite.VaultId, invite.PairingToken, applicant, signature);
    }

    /// <summary>True when the applicant's device id is the fingerprint of its own key AND the signature
    /// verifies against that key over the canonical bytes.</summary>
    public static bool Verify(JoinRequest request)
    {
        var applicant = request.Applicant;
        if (applicant.DeviceId != Base32.Encode(SHA256.HashData(applicant.PublicKey))) return false;
        return DeviceIdentity.Verify(
            applicant.PublicKey, Canonical(request.VaultId, request.PresentedToken, applicant), request.Signature);
    }
}
