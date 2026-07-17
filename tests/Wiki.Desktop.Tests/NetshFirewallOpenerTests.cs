// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Desktop.Sync;
using Xunit;

namespace Wiki.Desktop.Tests;

/// <summary>The security-critical surface of the firewall opener is the netsh rule it builds: a scoping bug
/// (all ports, all protocols, all programs) would open the host far wider than intended. These lock the rule to
/// inbound TCP, this app's exe, and exactly the two sync ports. The elevated exec + COM probe can't run in a
/// unit test (they need admin + the real firewall), so they are covered by the two-machine runbook.</summary>
public class NetshFirewallOpenerTests
{
    [Fact]
    public void Add_rule_is_scoped_to_this_app_and_the_two_tcp_ports()
    {
        var args = NetshFirewallOpener.BuildAddArguments(@"C:\Apps\CollectiveWiki\CollectiveWiki.exe", 8768, 8767);
        Assert.Equal(
            "advfirewall firewall add rule name=\"CollectiveWiki Internet Sync\" dir=in action=allow " +
            "program=\"C:\\Apps\\CollectiveWiki\\CollectiveWiki.exe\" protocol=TCP localport=8768,8767 profile=any enable=yes",
            args);
    }

    [Fact]
    public void Add_rule_is_inbound_tcp_and_port_scoped()
    {
        var args = NetshFirewallOpener.BuildAddArguments(@"C:\a\b.exe", 8768, 8767);
        Assert.Contains("dir=in", args);
        Assert.Contains("action=allow", args);
        Assert.Contains("protocol=TCP", args);
        Assert.Contains("localport=8768,8767", args);          // exactly the two sync ports — not all ports
        Assert.Contains("program=\"C:\\a\\b.exe\"", args);      // and scoped to this app's exe
    }

    [Fact]
    public void Delete_rule_targets_the_same_named_rule()
    {
        Assert.Equal("advfirewall firewall delete rule name=\"CollectiveWiki Internet Sync\"", NetshFirewallOpener.BuildDeleteArguments());
        Assert.Contains(NetshFirewallOpener.RuleName, NetshFirewallOpener.BuildDeleteArguments());
    }

    [Fact]
    public async Task NoOp_opener_reports_success_and_does_nothing()
    {
        var op = new NoOpFirewallOpener();
        Assert.True(await op.EnsureInboundAllowedAsync(1, 2, CancellationToken.None));
        await op.RemoveAsync(CancellationToken.None);   // must not throw
    }
}
