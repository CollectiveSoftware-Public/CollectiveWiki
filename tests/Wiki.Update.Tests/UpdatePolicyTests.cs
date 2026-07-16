// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Update.Tests;

public class UpdatePolicyTests
{
    [Theory]
    [InlineData("1.1.0", "1.0.0", 1)]
    [InlineData("1.0.0", "1.1.0", -1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.10", "1.0.9", 1)]        // numeric, not lexical
    [InlineData("1.1.0", "1.1.0-rc.1", 1)]    // release > prerelease
    [InlineData("1.1.0-rc.2", "1.1.0-rc.1", 1)]
    public void CompareVersions_orders_semver(string a, string b, int sign)
        => Assert.Equal(sign, Math.Sign(UpdatePolicy.CompareVersions(a, b)));

    [Fact] public void ShouldOffer_only_when_newer_and_not_skipped()
    {
        Assert.True(UpdatePolicy.ShouldOffer("1.1.0", "1.0.0", null));
        Assert.False(UpdatePolicy.ShouldOffer("1.0.0", "1.0.0", null));    // same
        Assert.False(UpdatePolicy.ShouldOffer("0.9.0", "1.0.0", null));    // older
        Assert.False(UpdatePolicy.ShouldOffer("1.1.0", "1.0.0", "1.1.0")); // skipped
        Assert.True(UpdatePolicy.ShouldOffer("1.2.0", "1.0.0", "1.1.0"));  // newer than skip
    }

    [Fact] public void IsCheckDue_true_when_never_or_over_24h()
    {
        var now = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(UpdatePolicy.IsCheckDue(null, now));
        Assert.True(UpdatePolicy.IsCheckDue(now.AddHours(-25), now));
        Assert.False(UpdatePolicy.IsCheckDue(now.AddHours(-1), now));
    }
}
