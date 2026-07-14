// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Journal;
using Wiki.Core.Templating;

namespace Wiki.Core.Tests;

public class DailyNotesTests
{
    private static readonly DateTimeOffset Today = new(2026, 6, 28, 8, 0, 0, TimeSpan.Zero);

    private static DailyNotes Build(InMemoryVaultFileSystem fs, DailyNoteOptions opts)
        => new(fs, new FixedClock(Today), new TemplateEngine(), opts);

    [Fact]
    public void Resolves_path_from_folder_and_date_format()
    {
        var dn = Build(new InMemoryVaultFileSystem(), new DailyNoteOptions("journal", "yyyy-MM-dd", null));
        Assert.Equal("journal/2026-06-28.md", dn.ResolvePath(Today));
    }

    [Fact]
    public void Root_folder_yields_a_top_level_note()
    {
        var dn = Build(new InMemoryVaultFileSystem(), DailyNoteOptions.Default);
        Assert.Equal("2026-06-28.md", dn.ResolvePath(Today));
    }

    [Fact]
    public void GetOrCreateToday_creates_an_empty_note_when_absent()
    {
        var fs = new InMemoryVaultFileSystem();
        var dn = Build(fs, DailyNoteOptions.Default);

        string path = dn.GetOrCreateToday();

        Assert.Equal("2026-06-28.md", path);
        Assert.True(fs.Exists("2026-06-28.md"));
    }

    [Fact]
    public void GetOrCreateToday_seeds_from_a_template_with_title_and_date()
    {
        var fs = new InMemoryVaultFileSystem(new Dictionary<string, string>
        {
            ["templates/Daily.md"] = "# {{title}}\n\nLogged {{date}}.",
        });
        var dn = Build(fs, new DailyNoteOptions("", "yyyy-MM-dd", "templates/Daily.md"));

        dn.GetOrCreateToday();

        Assert.Equal("# 2026-06-28\n\nLogged 2026-06-28.", fs.ReadAllText("2026-06-28.md"));
    }

    [Fact]
    public void GetOrCreateToday_does_not_overwrite_an_existing_note()
    {
        var fs = new InMemoryVaultFileSystem(new Dictionary<string, string>
        {
            ["2026-06-28.md"] = "my existing words",
        });
        var dn = Build(fs, DailyNoteOptions.Default);

        dn.GetOrCreateToday();

        Assert.Equal("my existing words", fs.ReadAllText("2026-06-28.md"));
    }
}
