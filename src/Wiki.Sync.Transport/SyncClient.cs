// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Transport;

/// <summary>Pulls a peer's changes over one authenticated stream and reconciles them into the local replica
/// through Plan B's <see cref="AuthenticatingReconciler"/>: fetch the signed index, fetch content for each
/// live entry, then verify-and-apply. Whole-file transfer (spec v1). Returns the authenticated report.</summary>
public sealed class SyncClient(AuthenticatingReconciler reconciler)
{
    private readonly AuthenticatingReconciler _reconciler = reconciler;

    public async Task<AuthenticatedReport> PullAsync(
        Stream stream, string remoteDeviceId, VaultReplica local, AuthorizedPeersList peers,
        string pinnedOwnerDeviceId, DateTimeOffset now, CancellationToken ct = default)
    {
        await SyncWire.WriteFrameAsync(stream, SyncWire.MessageType.GetIndex, Array.Empty<byte>(), ct);
        var (type, payload) = await SyncWire.ReadFrameAsync(stream, ct);
        if (type != SyncWire.MessageType.Index) throw new InvalidDataException($"expected Index, got {type}");
        var signedIndex = SyncWire.DecodeIndex(payload);

        var fetched = new Dictionary<string, string>();
        foreach (var entry in signedIndex)
        {
            if (entry.Entry.Deleted) continue;
            await SyncWire.WriteFrameAsync(stream, SyncWire.MessageType.GetContent,
                SyncWire.EncodePath(entry.Entry.Path), ct);
            var (rtype, cpayload) = await SyncWire.ReadFrameAsync(stream, ct);
            if (rtype != SyncWire.MessageType.Content) throw new InvalidDataException($"expected Content, got {rtype}");
            if (SyncWire.DecodeContent(cpayload) is { } text) fetched[entry.Entry.Path] = text;
        }

        await SyncWire.WriteFrameAsync(stream, SyncWire.MessageType.Close, Array.Empty<byte>(), ct);

        return _reconciler.Reconcile(local, peers, pinnedOwnerDeviceId, remoteDeviceId,
            signedIndex, new PulledContent(fetched), now);
    }

    /// <summary>Serves the content this pull already fetched to the reconciler's hash-verification step.</summary>
    private sealed class PulledContent(IReadOnlyDictionary<string, string> map) : IRemoteContent
    {
        public string? Fetch(string path) => map.TryGetValue(path, out var c) ? c : null;
    }
}
