// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Desktop.Update;
using Xunit;

namespace Wiki.Desktop.Tests;

public class UpdateConfigTests
{
    [Fact] public void Feed_carries_the_embedded_key_urls_rid_and_skip()
    {
        var feed = UpdateConfig.BuildFeed("1.1.0", "win-x64", "1.2.0");
        Assert.Contains("releases/latest/download/manifest.json", feed.ManifestUrl.ToString());
        Assert.EndsWith(".sig", feed.SignatureUrl.ToString());
        Assert.Single(feed.TrustedKeys);
        Assert.StartsWith("MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcD", feed.TrustedKeys[0]);  // the embedded SPKI key
        Assert.Equal("win-x64", feed.Rid);
        Assert.Equal("1.1.0", feed.CurrentVersion);
        Assert.Equal("1.2.0", feed.SkippedVersion);
    }

    [Fact] public void CurrentRid_is_a_supported_platform()
        => Assert.Contains(UpdateConfig.CurrentRid(), new[] { "win-x64", "linux-x64", "osx-x64", "osx-arm64" });
}
