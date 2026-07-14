// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Models;
using Wiki.Desktop.ViewModels;
using Xunit;

namespace Wiki.Desktop.Tests;

public class BacklinkRowTests
{
    private static Backlink Link(string fromNote) => new(fromNote, new WikiLink("x", null, null, false, 0, 0));

    [Fact]
    public void From_strips_folder_and_md_extension()
    {
        var row = BacklinkRow.From(Link("Machines/Bench Vise.md"));
        Assert.Equal("Bench Vise", row.Title);
        Assert.Equal("Machines/Bench Vise.md", row.NotePath);   // navigation target keeps the full path
    }

    [Fact]
    public void From_handles_a_root_note()
    {
        var row = BacklinkRow.From(Link("Home.md"));
        Assert.Equal("Home", row.Title);
        Assert.Equal("Home.md", row.NotePath);
    }

    [Fact]
    public void From_handles_a_deeply_nested_note()
    {
        var row = BacklinkRow.From(Link("A/B/C/Deep Note.md"));
        Assert.Equal("Deep Note", row.Title);
        Assert.Equal("A/B/C/Deep Note.md", row.NotePath);
    }
}
