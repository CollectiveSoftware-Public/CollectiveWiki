// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Desktop.Sync;
using Xunit;

namespace Wiki.Desktop.Tests;

public class SyncDiscoveryHintTests
{
    [Fact]
    public void Round_trips_host_and_ports()
    {
        var hint = SyncDiscoveryHint.Format("192.168.1.20", 8768, 8767);
        Assert.True(SyncDiscoveryHint.TryParse(hint, out var host, out var pair, out var sync));
        Assert.Equal("192.168.1.20", host);
        Assert.Equal(8768, pair);
        Assert.Equal(8767, sync);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("lan/host/notaport/8767")]
    public void Rejects_malformed_hints(string hint)
        => Assert.False(SyncDiscoveryHint.TryParse(hint, out _, out _, out _));
}
