// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Desktop;
using Xunit;

namespace Wiki.Desktop.Tests;

public class CommandRegistryTests
{
    private static CommandRegistry.AppCommand Cmd(string id, string label)
        => new(id, label, null, () => { });

    [Fact]
    public void Filter_ranks_prefix_before_substring_and_drops_non_matches()
    {
        var all = new[] { Cmd("a", "New Note"), Cmd("b", "Open Vault"), Cmd("c", "Note Settings") };
        var r = CommandRegistry.Filter(all, "note");
        Assert.Equal(2, r.Count);
        Assert.Equal("Note Settings", r[0].Label);   // prefix (rank 1) beats "New Note" substring (rank 2)
        Assert.Equal("New Note", r[1].Label);
        Assert.DoesNotContain(r, c => c.Id == "b");   // "Open Vault" doesn't contain "note"
    }

    [Fact]
    public void Empty_query_returns_all_commands_in_original_order()
    {
        var all = new[] { Cmd("a", "New Note"), Cmd("b", "Open Vault") };
        var r = CommandRegistry.Filter(all, "");
        Assert.Equal(2, r.Count);
        Assert.Equal("New Note", r[0].Label);
    }

    [Fact]
    public void Filter_matches_case_insensitively()
        => Assert.Single(CommandRegistry.Filter(new[] { Cmd("a", "Daily Note"), Cmd("b", "Open Vault") }, "DAILY"));
}
