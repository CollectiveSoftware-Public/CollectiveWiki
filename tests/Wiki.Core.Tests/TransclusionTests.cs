// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Embedding;
using Wiki.Core.Indexing;
using Wiki.Core.Models;
using Wiki.Core.Parsing;

namespace Wiki.Core.Tests;

public class TransclusionTests
{
    private static TransclusionResolver Build(InMemoryVaultFileSystem fs)
        => new(fs, new LinkResolver(fs), new HeadingSectionExtractor(new MarkdigMarkdownParser()));

    private static WikiLink Embed(string target, string? heading = null)
        => new(target, heading, null, IsEmbed: true, 0, 0);

    [Fact]
    public void Whole_note_embed_returns_full_content()
    {
        var fs = new InMemoryVaultFileSystem(new Dictionary<string, string> { ["Note.md"] = "# Note\n\nbody" });
        var t = Build(fs).Resolve(Embed("Note"));
        Assert.Equal(TransclusionKind.Note, t.Kind);
        Assert.Equal("Note.md", t.ResolvedPath);
        Assert.Equal("# Note\n\nbody", t.Content);
    }

    [Fact]
    public void Section_embed_returns_just_that_section()
    {
        var fs = new InMemoryVaultFileSystem(new Dictionary<string, string>
        {
            ["Note.md"] = "# A\n\nalpha\n\n## B\n\nbeta\n\n## C\n\ngamma",
        });
        var t = Build(fs).Resolve(Embed("Note", "B"));
        Assert.Equal(TransclusionKind.Section, t.Kind);
        Assert.Contains("beta", t.Content);
        Assert.DoesNotContain("gamma", t.Content);
    }

    [Fact]
    public void Image_embed_is_classified_as_image()
    {
        var fs = new InMemoryVaultFileSystem();
        var t = Build(fs).Resolve(Embed("diagram.png"));
        Assert.Equal(TransclusionKind.Image, t.Kind);
        Assert.Equal("diagram.png", t.ResolvedPath);
        Assert.Null(t.Content);
    }

    [Fact]
    public void Missing_note_or_missing_heading_is_unresolved()
    {
        var fs = new InMemoryVaultFileSystem(new Dictionary<string, string> { ["Note.md"] = "# A\n\nalpha" });
        Assert.Equal(TransclusionKind.Unresolved, Build(fs).Resolve(Embed("Ghost")).Kind);
        Assert.Equal(TransclusionKind.Unresolved, Build(fs).Resolve(Embed("Note", "Nope")).Kind);
    }
}
