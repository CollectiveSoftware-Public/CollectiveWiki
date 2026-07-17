// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Linq;
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

    public static IEnumerable<object[]> AllOutcomes() =>
        Enum.GetValues<PairingOutcome>().Select(o => new object[] { o });

    // General invariants for EVERY outcome (including any added later): the user always gets a real, friendly
    // sentence, and it never leaks the raw enum member name (e.g. "NoRoute"/"OwnerUnreachable") to the UI.
    [Theory]
    [MemberData(nameof(AllOutcomes))]
    public void Every_outcome_maps_to_a_non_empty_message_that_hides_the_enum_name(PairingOutcome outcome)
    {
        var msg = PairingOutcomeMessages.For(outcome);
        Assert.False(string.IsNullOrWhiteSpace(msg), $"{outcome} has no message");
        Assert.DoesNotContain(outcome.ToString(), msg, StringComparison.Ordinal);
    }
}
