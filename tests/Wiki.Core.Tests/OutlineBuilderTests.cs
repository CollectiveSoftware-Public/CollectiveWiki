// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Outline;

namespace Wiki.Core.Tests;

public class OutlineBuilderTests
{
    [Fact]
    public void Extracts_atx_headings_with_level_title_and_offset()
    {
        const string note = "# Title\n\nintro\n## Section A\ntext\n### Sub\n";
        var o = OutlineBuilder.Build(note);
        Assert.Equal(3, o.Count);
        Assert.Equal((1, "Title", 0), (o[0].Level, o[0].Title, o[0].Offset));
        Assert.Equal(2, o[1].Level);
        Assert.Equal("Section A", o[1].Title);
        Assert.Equal(note.IndexOf("## Section A"), o[1].Offset);
        Assert.Equal("Sub", o[2].Title);
    }

    [Fact]
    public void Ignores_hashes_inside_fenced_code()
    {
        const string note = "# Real\n```\n# not a heading\n```\n";
        var o = OutlineBuilder.Build(note);
        Assert.Single(o);
        Assert.Equal("Real", o[0].Title);
    }

    [Fact]
    public void No_headings_yields_empty()
        => Assert.Empty(OutlineBuilder.Build("just text\nmore text"));
}
