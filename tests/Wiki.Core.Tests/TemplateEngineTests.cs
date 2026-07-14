// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Templating;

namespace Wiki.Core.Tests;

public class TemplateEngineTests
{
    private static readonly TemplateContext Ctx =
        new("My Title", new DateTimeOffset(2026, 6, 28, 14, 5, 0, TimeSpan.Zero));

    private readonly ITemplateEngine _engine = new TemplateEngine();

    [Fact]
    public void Replaces_title_date_and_time()
    {
        Assert.Equal("# My Title", _engine.Render("# {{title}}", Ctx));
        Assert.Equal("2026-06-28", _engine.Render("{{date}}", Ctx));
        Assert.Equal("14:05", _engine.Render("{{time}}", Ctx));
    }

    [Fact]
    public void Honours_custom_format_specifiers()
    {
        Assert.Equal("2026", _engine.Render("{{date:yyyy}}", Ctx));
        Assert.Equal("Jun", _engine.Render("{{date:MMM}}", Ctx));
        Assert.Equal("02:05 PM", _engine.Render("{{time:hh:mm tt}}", Ctx));
    }

    [Fact]
    public void Unknown_placeholders_are_left_verbatim_and_whitespace_is_tolerated()
    {
        Assert.Equal("{{author}}", _engine.Render("{{author}}", Ctx));
        Assert.Equal("My Title", _engine.Render("{{ title }}", Ctx));
    }

    [Fact]
    public void Multiple_placeholders_in_one_template()
    {
        Assert.Equal("My Title — 2026-06-28",
            _engine.Render("{{title}} — {{date}}", Ctx));
    }
}
