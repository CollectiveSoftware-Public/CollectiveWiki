// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Xunit;

namespace Wiki.Desktop.Tests;

public class AppSettingsUpdateFieldsTests
{
    [Fact] public void Defaults_are_unset_and_survive_a_json_round_trip()
    {
        var s = new AppSettings();
        Assert.Equal("Unset", s.UpdateCheckMode);       // first run prompts
        Assert.Null(s.LastUpdateCheckUtc);
        Assert.Null(s.SkippedVersion);

        s.UpdateCheckMode = "Automatic";
        s.SkippedVersion = "1.2.0";
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;
        Assert.Equal("Automatic", back.UpdateCheckMode);
        Assert.Equal("1.2.0", back.SkippedVersion);
    }
}
