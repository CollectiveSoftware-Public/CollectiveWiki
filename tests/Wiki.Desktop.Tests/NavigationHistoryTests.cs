// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Desktop;
using Xunit;

namespace Wiki.Desktop.Tests;

public class NavigationHistoryTests
{
    [Fact]
    public void Back_and_forward_walk_the_visit_order()
    {
        var h = new NavigationHistory();
        h.Visit("a.md"); h.Visit("b.md"); h.Visit("c.md");
        Assert.Equal("b.md", h.GoBack());
        Assert.Equal("a.md", h.GoBack());
        Assert.Null(h.GoBack());
        Assert.Equal("b.md", h.GoForward());
        Assert.Equal("c.md", h.GoForward());
        Assert.Null(h.GoForward());
    }

    [Fact]
    public void Visit_after_back_drops_the_forward_tail()
    {
        var h = new NavigationHistory();
        h.Visit("a.md"); h.Visit("b.md");
        h.GoBack();
        h.Visit("c.md");
        Assert.False(h.CanGoForward);
        Assert.Equal("a.md", h.GoBack());
    }

    [Fact]
    public void Visiting_the_same_path_twice_collapses_to_one_entry()
    {
        var h = new NavigationHistory();
        h.Visit("a.md"); h.Visit("a.md"); h.Visit("b.md");
        Assert.Equal("a.md", h.GoBack());
        Assert.Null(h.GoBack());
    }

    [Fact]
    public void Clear_empties_everything()
    {
        var h = new NavigationHistory();
        h.Visit("a.md"); h.Visit("b.md");
        h.Clear();
        Assert.False(h.CanGoBack);
        Assert.False(h.CanGoForward);
        Assert.Null(h.GoBack());
    }

    [Fact]
    public void Empty_or_null_like_paths_are_ignored()
    {
        var h = new NavigationHistory();
        h.Visit("");
        Assert.False(h.CanGoBack);
        Assert.Null(h.GoBack());
    }
}
