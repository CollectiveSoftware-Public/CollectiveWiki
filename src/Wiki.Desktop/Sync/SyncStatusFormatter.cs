// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Sync;

namespace Wiki.Desktop.Sync;

/// <summary>Turns raw sync state into the one-line status the status bar shows. Pure + unit-tested; the
/// view calls it whenever sync state changes (dropping the old bare-enum "Sync: Idle" string).</summary>
public static class SyncStatusFormatter
{
    /// <param name="status">The engine's current phase.</param>
    /// <param name="lastSynced">When the last successful pull completed (null = never synced this session).</param>
    /// <param name="onlinePeers">How many collaborators were reachable on the last sync.</param>
    public static string Summarize(SyncStatus status, DateTimeOffset? lastSynced, int onlinePeers) => status switch
    {
        SyncStatus.Syncing => "Syncing…",
        SyncStatus.Offline => "Offline — will retry",
        _ when lastSynced is null => "Sync ready",
        _ => $"Synced {lastSynced.Value.ToLocalTime():HH:mm} · {onlinePeers} {(onlinePeers == 1 ? "peer" : "peers")}",
    };
}
