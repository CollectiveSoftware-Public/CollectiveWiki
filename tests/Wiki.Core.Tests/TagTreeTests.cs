// SPDX-License-Identifier: GPL-3.0-or-later
using System.Linq;
using Wiki.Core.Models;
using Xunit;

namespace Wiki.Core.Tests;

public class TagTreeTests
{
    [Fact]
    public void Builds_nested_tree_with_aggregated_counts()
    {
        var roots = TagTree.Build(new[] { ("area/work", 2), ("area/home", 1), ("misc", 5) });
        Assert.Equal(new[] { "area", "misc" }, roots.Select(r => r.Segment).ToArray());
        var area = roots[0];
        Assert.Equal("area", area.FullPath);
        Assert.Equal(0, area.OwnCount);         // no note tagged exactly #area
        Assert.Equal(3, area.TotalCount);       // work(2) + home(1)
        Assert.Equal(new[] { "home", "work" }, area.Children.Select(c => c.Segment).ToArray());
        Assert.Equal("area/work", area.Children.Single(c => c.Segment == "work").FullPath);
        Assert.Equal(5, roots[1].OwnCount);
    }

    [Fact]
    public void Flat_tags_stay_flat()
    {
        var roots = TagTree.Build(new[] { ("b", 1), ("a", 1) });
        Assert.Equal(new[] { "a", "b" }, roots.Select(r => r.Segment).ToArray());
        Assert.All(roots, r => Assert.Empty(r.Children));
    }
}
