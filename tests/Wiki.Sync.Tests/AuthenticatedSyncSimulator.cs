// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

/// <summary>Drives two authenticated peers to a fixpoint: each round, every peer signs its current index
/// and the other authenticates+reconciles it. Content is re-signed every round because merges produce
/// new content. Convergence = both peers hold identical live content for every path.</summary>
internal static class AuthenticatedSyncSimulator
{
    internal sealed record Peer(DeviceIdentity Identity, VaultReplica Replica, ChangeSigner Signer);

    internal static Peer NewPeer(DeviceIdentity id)
        => new(id, new VaultReplica(id.DeviceId), new ChangeSigner(id));

    private sealed class LiveContent(VaultReplica peer) : IRemoteContent
    {
        public string? Fetch(string path) => peer.Read(path);
    }

    internal static void RunToFixpoint(
        Peer a, Peer b, AuthorizedPeersList peers, string owner, DateTimeOffset now, int maxRounds = 20)
    {
        var reconciler = new AuthenticatingReconciler(new Reconciler(new Diff3MergeAdapter()), new ChangeVerifier());
        for (int round = 0; round < maxRounds; round++)
        {
            reconciler.Reconcile(a.Replica, peers, owner, b.Identity.DeviceId,
                b.Signer.SignIndex(b.Replica.Index), new LiveContent(b.Replica), now);
            reconciler.Reconcile(b.Replica, peers, owner, a.Identity.DeviceId,
                a.Signer.SignIndex(a.Replica.Index), new LiveContent(a.Replica), now);

            if (Converged(a.Replica, b.Replica)) return;
        }
        throw new InvalidOperationException($"did not converge within {maxRounds} rounds");
    }

    internal static bool Converged(VaultReplica a, VaultReplica b)
    {
        var paths = new HashSet<string>(a.Paths);
        paths.UnionWith(b.Paths);
        return paths.All(p => a.Read(p) == b.Read(p));
    }
}
