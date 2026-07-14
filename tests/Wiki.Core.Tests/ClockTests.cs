// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Time;

namespace Wiki.Core.Tests;

public class ClockTests
{
    [Fact]
    public void Fixed_clock_returns_its_instant()
    {
        var c = new FixedClock(new DateTimeOffset(2026, 6, 28, 9, 30, 0, TimeSpan.Zero));
        Assert.Equal(2026, c.Now.Year);
        Assert.Equal(30, c.Now.Minute);
    }

    [Fact]
    public void System_clock_is_close_to_wall_time()
    {
        var delta = (DateTimeOffset.Now - new SystemClock().Now).Duration();
        Assert.True(delta < TimeSpan.FromSeconds(5));
    }
}
