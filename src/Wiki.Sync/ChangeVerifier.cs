// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Sync;

/// <summary>The outcome of verifying one signed change against the authorized-peers list.</summary>
public enum ChangeVerdict { Accepted, Unauthorized, ReadOnlyDenied, TamperedMetadata }

/// <summary>Authorizes a single signed change: the signer must be listed (authorized), must not be
/// read-only, and its signature must verify against its recorded public key. Content integrity (hash
/// match) is enforced separately by <see cref="AuthenticatingReconciler"/> when content is fetched.</summary>
public sealed class ChangeVerifier
{
    public ChangeVerdict Verify(AuthorizedPeersList peers, SignedFileEntry signed)
    {
        var author = peers.Find(signed.Signer);
        if (author is null) return ChangeVerdict.Unauthorized;
        if (author.Role == PeerRole.ReadOnly) return ChangeVerdict.ReadOnlyDenied;

        var canonical = ChangeCanonical.Bytes(signed.Entry, signed.Signer);
        return DeviceIdentity.Verify(author.PublicKey, canonical, signed.Signature)
            ? ChangeVerdict.Accepted
            : ChangeVerdict.TamperedMetadata;
    }
}
