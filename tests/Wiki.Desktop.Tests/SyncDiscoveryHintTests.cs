// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using Wiki.Desktop.Sync;
using Xunit;

namespace Wiki.Desktop.Tests;

public class SyncDiscoveryHintTests
{
    [Fact]
    public void FormatMany_roundtrips_multiple_candidates()
    {
        var cands = new List<SyncCandidate>
        {
            new("192.168.1.5", 8768, 8767),
            new("2001:db8::1", 8768, 8767),
            new("203.0.113.9", 40000, 40001),
        };
        Assert.True(SyncDiscoveryHint.TryParseMany(SyncDiscoveryHint.FormatMany(cands), out var back));
        Assert.Equal(cands, back);
    }

    [Fact]
    public void TryParseMany_reads_a_legacy_single_lan_hint()
    {
        // A hint minted by the old code must still parse as one candidate.
        var legacy = SyncDiscoveryHint.Format("192.168.1.5", 8768, 8767);
        Assert.True(SyncDiscoveryHint.TryParseMany(legacy, out var back));
        Assert.Equal(new SyncCandidate("192.168.1.5", 8768, 8767), Assert.Single(back));
    }

    [Fact]
    public void TryParseMany_rejects_garbage()
    {
        Assert.False(SyncDiscoveryHint.TryParseMany("", out _));
        Assert.False(SyncDiscoveryHint.TryParseMany("nope", out _));
        Assert.False(SyncDiscoveryHint.TryParseMany("v2/192.168.1.5/notaport/8767", out _));
    }
}
