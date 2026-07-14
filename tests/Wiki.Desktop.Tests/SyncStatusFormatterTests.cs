// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using Wiki.Core.Sync;
using Wiki.Desktop.Sync;
using Xunit;

namespace Wiki.Desktop.Tests;

public class SyncStatusFormatterTests
{
    // A local DateTimeOffset so ToLocalTime() is identity and the HH:mm assertion is timezone-independent.
    private static readonly DateTimeOffset At1432 = new(new DateTime(2026, 7, 1, 14, 32, 0, DateTimeKind.Local));

    [Fact]
    public void Syncing_shows_progress_ignoring_time()
        => Assert.Equal("Syncing…", SyncStatusFormatter.Summarize(SyncStatus.Syncing, At1432, 2));

    [Fact]
    public void Offline_shows_retry_ignoring_time()
        => Assert.Equal("Offline — will retry", SyncStatusFormatter.Summarize(SyncStatus.Offline, At1432, 2));

    [Fact]
    public void Idle_without_a_sync_time_is_ready()
        => Assert.Equal("Sync ready", SyncStatusFormatter.Summarize(SyncStatus.Idle, null, 0));

    [Fact]
    public void Idle_with_one_peer_is_singular()
        => Assert.Equal("Synced 14:32 · 1 peer", SyncStatusFormatter.Summarize(SyncStatus.Idle, At1432, 1));

    [Fact]
    public void Idle_with_multiple_peers_is_plural()
        => Assert.Equal("Synced 14:32 · 2 peers", SyncStatusFormatter.Summarize(SyncStatus.Idle, At1432, 2));
}
