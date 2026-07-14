// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Linq;
using Wiki.Desktop.Sync;
using Wiki.Sync;
using Xunit;

namespace Wiki.Desktop.Tests;

public class PairingOutcomeMessagesTests
{
    [Theory]
    [InlineData(PairingOutcome.Accepted)]
    [InlineData(PairingOutcome.UnknownToken)]
    [InlineData(PairingOutcome.Expired)]
    [InlineData(PairingOutcome.AlreadyUsed)]
    [InlineData(PairingOutcome.WrongVault)]
    [InlineData(PairingOutcome.InvalidSignature)]
    [InlineData(PairingOutcome.IdentityMismatch)]
    public void Every_outcome_has_a_nonempty_sentence(PairingOutcome outcome)
        => Assert.False(string.IsNullOrWhiteSpace(PairingOutcomeMessages.For(outcome)));

    [Fact]
    public void Messages_are_distinct_across_outcomes()
    {
        var all = Enum.GetValues<PairingOutcome>().Select(PairingOutcomeMessages.For).ToArray();
        Assert.Equal(all.Length, all.Distinct().Count());
    }

    // The invite-problem cases must tell the user what to do next (ask the owner / re-paste the link),
    // never surface the raw enum name.
    [Theory]
    [InlineData(PairingOutcome.UnknownToken)]
    [InlineData(PairingOutcome.Expired)]
    [InlineData(PairingOutcome.AlreadyUsed)]
    [InlineData(PairingOutcome.WrongVault)]
    public void Invite_problems_are_actionable(PairingOutcome outcome)
    {
        var msg = PairingOutcomeMessages.For(outcome);
        Assert.True(
            msg.Contains("owner", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("paste", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("link", StringComparison.OrdinalIgnoreCase),
            $"expected an actionable hint in: {msg}");
        Assert.DoesNotContain(outcome.ToString(), msg, StringComparison.Ordinal);
    }
}
