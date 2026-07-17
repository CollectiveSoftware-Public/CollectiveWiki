// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using Wiki.Sync.Transport;
using Xunit;

namespace Wiki.Sync.Transport.Tests;

public class AddressScopeTests
{
    [Theory]
    [InlineData("203.0.113.9", true)]     // public IPv4
    [InlineData("2001:db8::1", true)]     // global IPv6 (documentation range, but global-scope)
    [InlineData("192.168.1.5", false)]    // RFC1918
    [InlineData("10.0.0.1", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("169.254.0.1", false)]    // link-local IPv4
    [InlineData("127.0.0.1", false)]      // loopback
    [InlineData("::1", false)]            // IPv6 loopback
    [InlineData("fe80::1", false)]        // IPv6 link-local
    [InlineData("fc00::1", false)]        // IPv6 ULA
    public void IsGlobal_classifies(string ip, bool expected)
        => Assert.Equal(expected, AddressScope.IsGlobal(IPAddress.Parse(ip)));
}
