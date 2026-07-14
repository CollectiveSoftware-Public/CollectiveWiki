// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Sync;

/// <summary>The result of an authenticated reconcile: the inner Plan A report plus how many incoming
/// changes were rejected and why. <see cref="ListRejected"/> is true when the peers list itself failed
/// verification, in which case nothing was applied.</summary>
public sealed record AuthenticatedReport(
    ReconcileReport Inner, int Unauthorized, int ReadOnlyDenied, int Tampered, bool ListRejected);

/// <summary>Wraps Plan A's <see cref="Reconciler"/> with authentication and authorization. It verifies
/// the owner-signed peers list, then admits only changes whose signer is authorized (not read-only) and
/// whose signature and fetched-content hash both check out — handing the survivors to the pure reconciler.
/// Tampered, unauthorized, and read-only changes never reach the replica.</summary>
public sealed class AuthenticatingReconciler(Reconciler inner, ChangeVerifier verifier)
{
    private readonly Reconciler _inner = inner;
    private readonly ChangeVerifier _verifier = verifier;

    public AuthenticatedReport Reconcile(
        VaultReplica local, AuthorizedPeersList peers, string pinnedOwnerDeviceId,
        string remoteDeviceId, IReadOnlyCollection<SignedFileEntry> signedIndex,
        IRemoteContent remote, DateTimeOffset now)
    {
        if (!peers.Verify(pinnedOwnerDeviceId))
            return new AuthenticatedReport(new ReconcileReport(0, 0, 0, 0), 0, 0, 0, ListRejected: true);

        int unauthorized = 0, readOnlyDenied = 0, tampered = 0;
        var accepted = new List<FileEntry>();
        var verifiedContent = new Dictionary<string, string>();

        foreach (var signed in signedIndex)
        {
            switch (_verifier.Verify(peers, signed))
            {
                case ChangeVerdict.Unauthorized: unauthorized++; continue;
                case ChangeVerdict.ReadOnlyDenied: readOnlyDenied++; continue;
                case ChangeVerdict.TamperedMetadata: tampered++; continue;
            }

            var entry = signed.Entry;
            if (!entry.Deleted)
            {
                var content = remote.Fetch(entry.Path);
                if (content is null || ContentHash.Of(content) != entry.ContentHash)
                {
                    tampered++;
                    continue;   // the delivered bytes don't match the signed hash → reject
                }
                verifiedContent[entry.Path] = content;
            }
            accepted.Add(entry);
        }

        var report = _inner.Reconcile(local, remoteDeviceId, accepted, new MapContent(verifiedContent), now);
        return new AuthenticatedReport(report, unauthorized, readOnlyDenied, tampered, ListRejected: false);
    }

    /// <summary>Serves only the pre-verified content the reconciler is allowed to adopt.</summary>
    private sealed class MapContent(IReadOnlyDictionary<string, string> map) : IRemoteContent
    {
        public string? Fetch(string path) => map.TryGetValue(path, out var c) ? c : null;
    }
}
