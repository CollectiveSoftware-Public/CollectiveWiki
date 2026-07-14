// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Indexing;
using Wiki.Core.Parsing;
using Wiki.Core.Search;
using Wiki.Core.Workspace;
using Xunit;

namespace Wiki.Core.Tests;

public class VaultSessionTemplateTests
{
    private static VaultSession New(out InMemoryVaultFileSystem fs)
    {
        fs = new InMemoryVaultFileSystem(new Dictionary<string, string>
        {
            ["Home.md"] = "# Home\n",
            ["templates/Meeting.md"] = "# {{title}}\nDate: {{date}}\nNotes:\n",
        });
        var parser = new MarkdigMarkdownParser();
        var resolver = new LinkResolver(fs, parser);
        var index = new WikiIndex(fs, parser, resolver, new InMemoryFtsIndex());
        index.Rebuild();
        return new VaultSession(fs, index, resolver);
    }

    [Fact]
    public void ListTemplates_returns_template_names_without_extension()
        => Assert.Equal(new[] { "Meeting" }, New(out _).ListTemplates());

    [Fact]
    public void RenderTemplate_substitutes_the_title_and_date_placeholders()
    {
        var rendered = New(out _).RenderTemplate("Meeting", "Standup");
        Assert.Contains("# Standup", rendered);
        Assert.DoesNotContain("{{title}}", rendered);
        Assert.DoesNotContain("{{date}}", rendered);
    }

    [Fact]
    public void RenderTemplate_returns_empty_for_a_missing_template()
        => Assert.Equal("", New(out _).RenderTemplate("Nope", "x"));

    [Fact]
    public void CreateFromTemplate_writes_a_new_note_with_the_rendered_body()
    {
        var s = New(out var fs);
        string path = s.CreateFromTemplate("Meeting", "Standup", "");
        Assert.Equal("Standup.md", path);
        Assert.Contains("# Standup", fs.ReadAllText(path));
    }

    [Fact]
    public void CreateFromTemplate_places_the_note_in_a_folder_and_disambiguates()
    {
        var s = New(out _);
        Assert.Equal("Meetings/Standup.md", s.CreateFromTemplate("Meeting", "Standup", "Meetings"));
        Assert.Equal("Meetings/Standup 2.md", s.CreateFromTemplate("Meeting", "Standup", "Meetings"));
    }
}
