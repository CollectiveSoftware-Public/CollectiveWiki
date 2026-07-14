// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Editor;
using Wiki.Core.Models;
using Xunit;

namespace Wiki.Editor.Tests;

public class CheckboxRenderTests
{
    private static RenderedLine Build(string line)
        => RenderedLineBuilder.Build(line, 0,
            new[] { new DecorationSpan(0, line.Length, SpanKind.ListItem) },
            System.Array.Empty<WikiLink>());

    [Fact]
    public void Unchecked_task_renders_a_checkbox_run_before_the_text()
    {
        var r = Build("- [ ] Buy milk");
        var box = r.Runs.First();
        Assert.Equal(RunStyle.Checkbox, box.Style);
        Assert.Equal("☐ ", box.Text);                 // ☐
        Assert.Equal("3", box.LinkTarget);                 // the space between [ ] is at offset 3
        Assert.Contains(r.Runs, x => x.Text == "Buy milk");
        Assert.DoesNotContain(r.Runs, x => x.Text.Contains("[ ]"));
    }

    [Fact]
    public void Checked_task_renders_a_ticked_box()
    {
        var r = Build("- [x] Done");
        var box = r.Runs.First();
        Assert.Equal(RunStyle.Checkbox, box.Style);
        Assert.Equal("☑ ", box.Text);                 // ☑
        Assert.Equal("3", box.LinkTarget);
    }

    [Fact]
    public void Plain_list_item_is_unaffected()
    {
        var r = Build("- just a bullet");
        Assert.Equal(RunStyle.ListMarker, r.Runs.First().Style);
        Assert.DoesNotContain(r.Runs, x => x.Style == RunStyle.Checkbox);
    }
}
