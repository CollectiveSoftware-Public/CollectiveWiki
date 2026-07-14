// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Linq;
using Wiki.Editor;
using Xunit;

namespace Wiki.Editor.Tests;

public class FrontmatterModelTests
{
    private const string Note = "---\nstatus: in-progress\ncount: 3\ndue: 2026-07-20\ndone: true\ntags:\n  - kb\n  - ux\n---\nBody line\n";

    [Fact]
    public void Parse_InfersTypes()
    {
        var d = FrontmatterModel.Parse(Note);
        Assert.True(d.HasBlock);
        Assert.Equal(PropertyType.Text,     d.Properties.Single(p => p.Key == "status").Type);
        Assert.Equal(PropertyType.Number,   d.Properties.Single(p => p.Key == "count").Type);
        Assert.Equal(PropertyType.Date,     d.Properties.Single(p => p.Key == "due").Type);
        Assert.Equal(PropertyType.Checkbox, d.Properties.Single(p => p.Key == "done").Type);
        var tags = d.Properties.Single(p => p.Key == "tags");
        Assert.Equal(PropertyType.List, tags.Type);
        Assert.Equal(new[] { "kb", "ux" }, tags.Items.ToArray());
    }

    [Fact]
    public void Parse_FlowList()
    {
        var d = FrontmatterModel.Parse("---\ntags: [a, b, c]\n---\n");
        Assert.Equal(new[] { "a", "b", "c" }, d.Properties.Single().Items.ToArray());
    }

    [Fact]
    public void Parse_NoFrontmatter_HasNoBlock()
        => Assert.False(FrontmatterModel.Parse("just body\n").HasBlock);

    [Fact]
    public void ApplyTo_ReplacesTheBlock_KeepingTheBody()
    {
        var props = new[]
        {
            FrontmatterProperty.Scalar("status", PropertyType.Text, "done"),
            FrontmatterProperty.List("tags", new[] { "kb" }),
        };
        string result = FrontmatterModel.ApplyTo(Note, props);
        Assert.Equal("---\nstatus: done\ntags:\n  - kb\n---\nBody line\n", result);
    }

    [Fact]
    public void ApplyTo_NoExistingBlock_PrependsOne()
    {
        string result = FrontmatterModel.ApplyTo("Body only\n",
            new[] { FrontmatterProperty.Scalar("type", PropertyType.Text, "note") });
        Assert.Equal("---\ntype: note\n---\nBody only\n", result);
    }

    [Fact]
    public void ApplyTo_EmptyProps_RemovesTheBlock()
        => Assert.Equal("Body line\n", FrontmatterModel.ApplyTo(Note, Array.Empty<FrontmatterProperty>()));

    [Fact]
    public void SerializeBlock_QuotesValuesThatNeedIt()
        => Assert.Contains("title: \"a: b\"",
            FrontmatterModel.SerializeBlock(new[] { FrontmatterProperty.Scalar("title", PropertyType.Text, "a: b") }));
}
