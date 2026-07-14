// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Transport;

/// <summary>What a serving peer exposes to a puller: its signed index and its content per path.</summary>
public interface ISyncContentProvider
{
    IReadOnlyList<SignedFileEntry> Index();
    string? Content(string path);
}

/// <summary>Serves a live <see cref="VaultReplica"/>, signing its index as the given identity on each call
/// (so post-merge content is always re-signed with a current signature — matching the headless simulator).</summary>
public sealed class ReplicaContentProvider(VaultReplica replica, ChangeSigner signer) : ISyncContentProvider
{
    public IReadOnlyList<SignedFileEntry> Index() => signer.SignIndex(replica.Index);
    public string? Content(string path) => replica.Read(path);
}
