// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Sync;

/// <summary>Fetches a peer's current content for a path on demand (a transport call, or a direct read
/// in the headless simulation).</summary>
public interface IRemoteContent
{
    string? Fetch(string path);
}

/// <summary>Counts of what a single reconcile pass did, for assertions and status.</summary>
public sealed record ReconcileReport(int Adopted, int Merged, int Conflicted, int Resolved);

/// <summary>Applies a peer's index (and fetched content) into the local replica: adopt newer remote
/// state, leave newer local state for the peer to pull, and resolve concurrent edits. The algorithm is
/// symmetric and convergent — both peers reconciling to fixpoint reach identical state.</summary>
public sealed class Reconciler(IThreeWayMerger merger)
{
    private readonly IThreeWayMerger _merger = merger;

    public ReconcileReport Reconcile(VaultReplica local, string remoteDeviceId,
        IReadOnlyCollection<FileEntry> remoteIndex, IRemoteContent remote, DateTimeOffset now)
    {
        int adopted = 0, merged = 0, conflicted = 0, resolved = 0;
        var remoteByPath = remoteIndex.ToDictionary(e => e.Path);

        var allPaths = new HashSet<string>(local.Paths);
        allPaths.UnionWith(remoteByPath.Keys);

        foreach (var path in allPaths)
        {
            if (!remoteByPath.TryGetValue(path, out var r))
                continue;   // local-only — the peer will pull it from us on its pass

            var l = local.Find(path);
            if (l is null)
            {
                Adopt(local, path, r, remote);
                adopted++;
                continue;
            }

            switch (l.Version.CompareTo(r.Version))
            {
                case VectorOrdering.Equal:
                    local.ConfirmBase(path);   // both peers agree on this state → it becomes the common ancestor for future merges
                    break;
                case VectorOrdering.Dominates:
                    break;   // local is ahead; the peer pulls, then a later round reaches Equal
                case VectorOrdering.DominatedBy:
                    Adopt(local, path, r, remote);
                    adopted++;
                    break;
                case VectorOrdering.Concurrent:
                    switch (ResolveConcurrent(local, remoteDeviceId, path, l, r, remote, now))
                    {
                        case Resolution.Merged: merged++; break;
                        case Resolution.Conflicted: conflicted++; break;
                        case Resolution.Resolved: resolved++; break;
                    }
                    break;
            }
        }

        return new ReconcileReport(adopted, merged, conflicted, resolved);
    }

    private enum Resolution { Merged, Conflicted, Resolved }

    private Resolution ResolveConcurrent(VaultReplica local, string remoteDeviceId, string path,
        FileEntry l, FileEntry r, IRemoteContent remote, DateTimeOffset now)
    {
        var version = l.Version.Merge(r.Version);

        // Both deleted → stay deleted with the merged vector.
        if (l.Deleted && r.Deleted)
        {
            local.ApplyReconciled(path, null, version, true);
            return Resolution.Resolved;
        }

        // Delete vs edit → the edit wins (no data loss); drop the delete.
        if (l.Deleted ^ r.Deleted)
        {
            string? live = l.Deleted ? remote.Fetch(path) : local.Read(path);
            local.ApplyReconciled(path, live, version, false);
            return Resolution.Resolved;
        }

        // Both modified.
        string localContent = local.Read(path) ?? "";
        string remoteContent = remote.Fetch(path) ?? "";
        string? baseContent = local.BaseOf(path);

        if (baseContent is not null)
        {
            var attempt = _merger.Merge(baseContent, localContent, remoteContent);
            if (!attempt.HasConflicts)
            {
                local.ApplyReconciled(path, attempt.MergedText, version, false);
                return Resolution.Merged;
            }
        }

        // No common ancestor, or the merge conflicts → keep both, deterministically.
        bool localWins = string.CompareOrdinal(local.DeviceId, remoteDeviceId) <= 0;
        string winnerContent = localWins ? localContent : remoteContent;
        string loserContent = localWins ? remoteContent : localContent;
        string loserDevice = localWins ? remoteDeviceId : local.DeviceId;
        string copyPath = ConflictCopyName.For(path, loserDevice, now);

        local.ApplyReconciled(path, winnerContent, version, false);
        local.ApplyReconciled(copyPath, loserContent, version, false);
        return Resolution.Conflicted;
    }

    private static void Adopt(VaultReplica local, string path, FileEntry remote, IRemoteContent content)
    {
        if (remote.Deleted) local.ApplyReconciled(path, null, remote.Version, true);
        else local.ApplyReconciled(path, content.Fetch(path), remote.Version, false);
    }
}
