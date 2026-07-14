// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;

namespace Wiki.Sync;

/// <summary>A peer's role in a shared vault. Only <see cref="Owner"/> and <see cref="ReadWrite"/> may
/// author changes that honest peers apply; <see cref="ReadOnly"/> peers receive but cannot push.</summary>
public enum PeerRole { Owner, ReadWrite, ReadOnly }

/// <summary>One authorized participant: its pinned device id, public key (for verifying its signatures),
/// role, and git-style attribution (name/email — display only, never used for authorization).</summary>
public sealed record AuthorizedPeer(string DeviceId, byte[] PublicKey, PeerRole Role, string Name, string Email);

/// <summary>The owner's signed roster of who may participate and in what role — the root of authorization.
/// Distributed to all peers and pinned to the owner's device id. Verification checks: the pinned owner
/// matches, an Owner-role entry for that owner exists, every entry's device id equals the fingerprint of
/// its own public key (no identity spoofing), and the owner's signature covers the canonical bytes.</summary>
public sealed class AuthorizedPeersList
{
    public string OwnerDeviceId { get; }
    public IReadOnlyList<AuthorizedPeer> Peers { get; }
    public byte[] Signature { get; }

    public AuthorizedPeersList(string ownerDeviceId, IReadOnlyList<AuthorizedPeer> peers, byte[] signature)
    {
        OwnerDeviceId = ownerDeviceId;
        Peers = peers;
        Signature = signature;
    }

    public static AuthorizedPeersList Sign(DeviceIdentity owner, IReadOnlyList<AuthorizedPeer> peers)
        => new(owner.DeviceId, peers, owner.Sign(Canonical(owner.DeviceId, peers)));

    public AuthorizedPeer? Find(string deviceId)
    {
        foreach (var p in Peers)
            if (p.DeviceId == deviceId) return p;
        return null;
    }

    public PeerRole? RoleOf(string deviceId) => Find(deviceId)?.Role;

    public bool Verify(string pinnedOwnerDeviceId)
    {
        if (OwnerDeviceId != pinnedOwnerDeviceId) return false;

        var ownerPeer = Find(OwnerDeviceId);
        if (ownerPeer is null || ownerPeer.Role != PeerRole.Owner) return false;

        // No entry may claim a device id other than the fingerprint of its own key.
        foreach (var p in Peers)
            if (p.DeviceId != Base32.Encode(SHA256.HashData(p.PublicKey))) return false;

        return DeviceIdentity.Verify(ownerPeer.PublicKey, Canonical(OwnerDeviceId, Peers), Signature);
    }

    // Deterministic canonical bytes: peers sorted by ordinal device id; free-text fields length-prefixed
    // so distinct (name,email) tuples can't collide into one signable string.
    private static byte[] Canonical(string ownerDeviceId, IReadOnlyList<AuthorizedPeer> peers)
    {
        var sb = new StringBuilder();
        sb.Append("cwikipeers/v1\n").Append(ownerDeviceId).Append('\n');
        foreach (var p in peers.OrderBy(p => p.DeviceId, StringComparer.Ordinal))
        {
            sb.Append(p.DeviceId).Append('\t')
              .Append(Convert.ToHexString(p.PublicKey)).Append('\t')
              .Append((int)p.Role).Append('\t')
              .Append(p.Name.Length).Append(':').Append(p.Name).Append('\t')
              .Append(p.Email.Length).Append(':').Append(p.Email).Append('\n');
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
