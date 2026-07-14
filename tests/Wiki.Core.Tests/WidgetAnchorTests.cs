// SPDX-License-Identifier: GPL-3.0-or-later
using Code.Core.Text;
using Wiki.Core.Editor;
using Wiki.Core.Parsing;

namespace Wiki.Core.Tests;

public class WidgetAnchorTests
{
    private static DecorationPlan Plan(string text)
        => new EditorModel(new MarkdigMarkdownParser())
            .ComputePlan(new TextDocument(text), new SelectionSet(Selection.At(0)));

    [Fact]
    public void Transclusion_embed_anchors_at_its_offset()
    {
        const string text = "intro\n![[Other Note]]\nend";
        var w = Assert.Single(Plan(text).Widgets);
        Assert.Equal(WidgetKind.Transclusion, w.Kind);
        Assert.Equal("Other Note", w.Target);
        Assert.Equal("![[Other Note]]", text.Substring(w.Offset, "![[Other Note]]".Length));
    }

    [Fact]
    public void Image_embed_is_classified_as_image()
    {
        var w = Assert.Single(Plan("![[diagram.png]]").Widgets);
        Assert.Equal(WidgetKind.Image, w.Kind);
        Assert.Equal("diagram.png", w.Target);
        Assert.Equal(0, w.Offset);
    }

    [Fact]
    public void Plain_wikilinks_are_not_widgets()
        => Assert.Empty(Plan("see [[Note]] here").Widgets);
}
