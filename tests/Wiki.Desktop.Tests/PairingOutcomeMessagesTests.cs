// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Desktop.Sync;
using Wiki.Sync;
using Xunit;

namespace Wiki.Desktop.Tests;

public class PairingOutcomeMessagesTests
{
    [Fact]
    public void NoRoute_tells_the_user_the_owner_must_enable_internet_sync()
    {
        var msg = PairingOutcomeMessages.For(PairingOutcome.NoRoute);
        Assert.Contains("internet sync", msg);
    }

    [Fact]
    public void OwnerUnreachable_names_offline_or_carrier_nat()
    {
        var msg = PairingOutcomeMessages.For(PairingOutcome.OwnerUnreachable);
        Assert.Contains("offline", msg);
    }

    [Fact]
    public void Accepted_still_maps()
        => Assert.Equal("You're paired — syncing now.", PairingOutcomeMessages.For(PairingOutcome.Accepted));
}
