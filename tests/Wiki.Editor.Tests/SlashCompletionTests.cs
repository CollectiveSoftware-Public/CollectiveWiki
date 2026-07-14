// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Editor;
using Xunit;

namespace Wiki.Editor.Tests;

public class SlashCompletionTests
{
    [Fact] public void Detects_slash_at_line_start()
    {
        var ctx = SlashCompletion.Detect("/hea", 4);
        Assert.NotNull(ctx);
        Assert.Equal(0, ctx!.Value.SlashPos);
        Assert.Equal("hea", ctx.Value.Query);
    }

    [Fact] public void Detects_slash_after_leading_whitespace()
        => Assert.NotNull(SlashCompletion.Detect("  /ta", 5));

    [Fact] public void Does_not_trigger_mid_word()
        => Assert.Null(SlashCompletion.Detect("path/to", 7));   // '/' not at a line/word boundary

    [Fact] public void Ranks_candidates_by_query()
    {
        var c = SlashCommands.Candidates("table");
        Assert.Equal("table", c[0].Id);
    }

    [Fact] public void RemoveTrigger_strips_the_slash_query()
    {
        var ctx = SlashCompletion.Detect("/table", 6)!.Value;
        var (text, sel) = SlashCompletion.RemoveTrigger("/table", ctx, 6);
        Assert.Equal("", text);
        Assert.Equal(0, sel);
    }
}
