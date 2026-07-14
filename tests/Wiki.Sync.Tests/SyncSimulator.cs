// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

/// <summary>Drives two replicas through reconcile rounds until neither changes (a fixpoint), simulating
/// a real bidirectional sync without a network. Content is fetched directly from the peer replica.</summary>
public static class SyncSimulator
{
    private sealed class ReplicaContent(VaultReplica peer) : IRemoteContent
    {
        public string? Fetch(string path) => peer.Read(path);
    }

    public static int RunToFixpoint(VaultReplica a, VaultReplica b, DateTimeOffset now, int maxRounds = 20)
    {
        var rec = new Reconciler(new Diff3MergeAdapter());
        for (int round = 1; round <= maxRounds; round++)
        {
            var ra = rec.Reconcile(a, b.DeviceId, b.Index, new ReplicaContent(b), now);
            var rb = rec.Reconcile(b, a.DeviceId, a.Index, new ReplicaContent(a), now);
            if (IsNoOp(ra) && IsNoOp(rb) && Identical(a, b))
                return round;
        }
        throw new Xunit.Sdk.XunitException($"replicas did not converge within {maxRounds} rounds");
    }

    private static bool IsNoOp(ReconcileReport r)
        => r is { Adopted: 0, Merged: 0, Conflicted: 0, Resolved: 0 };

    private static bool Identical(VaultReplica a, VaultReplica b)
    {
        var pa = a.Index.OrderBy(e => e.Path).ToList();
        var pb = b.Index.OrderBy(e => e.Path).ToList();
        if (pa.Count != pb.Count) return false;
        for (int i = 0; i < pa.Count; i++)
        {
            if (pa[i].Path != pb[i].Path) return false;
            if (pa[i].Deleted != pb[i].Deleted) return false;
            if (a.Read(pa[i].Path) != b.Read(pb[i].Path)) return false;
        }
        return true;
    }

    public static void AssertConverged(VaultReplica a, VaultReplica b)
    {
        var pa = a.Index.Where(e => !e.Deleted).Select(e => e.Path).OrderBy(p => p).ToArray();
        var pb = b.Index.Where(e => !e.Deleted).Select(e => e.Path).OrderBy(p => p).ToArray();
        Xunit.Assert.Equal(pa, pb);
        foreach (var path in pa)
            Xunit.Assert.Equal(a.Read(path), b.Read(path));
    }
}
