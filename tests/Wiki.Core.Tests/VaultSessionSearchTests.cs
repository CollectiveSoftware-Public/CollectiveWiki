// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Indexing;
using Wiki.Core.Parsing;
using Wiki.Core.Search;
using Wiki.Core.Workspace;
using Xunit;

namespace Wiki.Core.Tests;

public class VaultSessionSearchTests
{
    private static VaultSession New()
    {
        var fs = new InMemoryVaultFileSystem(new Dictionary<string, string>
        {
            ["Alpha.md"] = "# Alpha\nThe quick brown fox jumps over the lazy dog.\n#animals",
            ["Beta.md"] = "# Beta\nNothing especially notable here.\n#misc",
        });
        var parser = new MarkdigMarkdownParser();
        var resolver = new LinkResolver(fs, parser);
        var index = new WikiIndex(fs, parser, resolver, new InMemoryFtsIndex());
        index.Rebuild();
        return new VaultSession(fs, index, resolver);
    }

    [Fact]
    public void SearchWithSnippets_returns_the_matching_note_with_a_line_snippet()
    {
        var results = New().SearchWithSnippets("quick");
        var alpha = Assert.Single(results, r => r.NotePath == "Alpha.md");
        Assert.Equal("Alpha", alpha.Title);
        Assert.Contains(alpha.Snippets, s => s.Text.Contains("quick brown fox"));
    }

    [Fact]
    public void SearchWithSnippets_empty_query_returns_nothing()
        => Assert.Empty(New().SearchWithSnippets(""));

    [Fact]
    public void AllTags_lists_every_distinct_tag_sorted()
        => Assert.Equal(new[] { "animals", "misc" }, New().AllTags());

    [Fact]
    public void NotesWithTag_finds_the_tagged_note()
        => Assert.Equal(new[] { "Alpha.md" }, New().NotesWithTag("animals"));
}
