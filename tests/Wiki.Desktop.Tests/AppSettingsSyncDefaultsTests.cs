// SPDX-License-Identifier: GPL-3.0-or-later
using Xunit;

namespace Wiki.Desktop.Tests;

public class AppSettingsSyncDefaultsTests
{
    [Fact]
    public void Sync_settings_have_sensible_defaults()
    {
        var s = new AppSettings();
        Assert.False(s.SyncEnabled);
        Assert.Null(s.SyncDeviceName);
        Assert.Equal(8767, s.SyncPort);
        Assert.Equal(8768, s.PairingPort);
    }
}
