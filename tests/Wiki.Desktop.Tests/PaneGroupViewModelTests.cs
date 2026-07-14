// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Desktop.ViewModels;
using Xunit;

namespace Wiki.Desktop.Tests;

public class PaneGroupViewModelTests
{
    [Fact]
    public void SplitRight_creates_the_right_pane_and_makes_it_active()
    {
        var g = new PaneGroupViewModel();
        g.Left.OpenNote("a.md", "a", activate: true);
        var right = g.SplitRight();
        right.OpenNote("b.md", "b", activate: true);
        Assert.True(g.IsSplit);
        Assert.Equal(PaneGroupViewModel.Side.Right, g.ActiveSide);
        Assert.Equal("b.md", g.Active!.NotePath);
    }

    [Fact]
    public void Focus_flips_the_active_pane()
    {
        var g = new PaneGroupViewModel();
        g.Left.OpenNote("a.md", "a", activate: true);
        g.SplitRight().OpenNote("b.md", "b", activate: true);
        g.Focus(PaneGroupViewModel.Side.Left);
        Assert.Equal("a.md", g.Active!.NotePath);
    }

    [Fact]
    public void CloseRight_collapses_back_to_a_single_pane()
    {
        var g = new PaneGroupViewModel();
        g.SplitRight();
        g.CloseRight();
        Assert.False(g.IsSplit);
        Assert.Equal(PaneGroupViewModel.Side.Left, g.ActiveSide);
    }

    [Fact]
    public void ActiveChanged_fires_on_split_and_focus()
    {
        var g = new PaneGroupViewModel();
        int n = 0; g.ActiveChanged += (_, _) => n++;
        g.SplitRight();
        g.Focus(PaneGroupViewModel.Side.Left);
        Assert.True(n >= 2);
    }
}
