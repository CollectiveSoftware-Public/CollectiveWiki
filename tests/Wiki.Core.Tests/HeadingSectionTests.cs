// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Embedding;
using Wiki.Core.Parsing;

namespace Wiki.Core.Tests;

public class HeadingSectionTests
{
    private readonly HeadingSectionExtractor _ex = new(new MarkdigMarkdownParser());

    private const string Doc =
        "# Top\n\nintro\n\n## Intro\n\nalpha beta\n\n### Sub\n\ndeep\n\n## Next\n\ngamma\n";

    [Fact]
    public void Extracts_a_section_up_to_the_next_same_level_heading()
    {
        var s = _ex.Extract(Doc, "Intro");
        Assert.NotNull(s);
        string body = Doc.Substring(s!.Start, s.End - s.Start);
        Assert.StartsWith("## Intro", body);
        Assert.Contains("alpha beta", body);
        Assert.Contains("### Sub", body);     // a deeper subsection is included
        Assert.Contains("deep", body);
        Assert.DoesNotContain("## Next", body); // stops at the next same-level heading
        Assert.DoesNotContain("gamma", body);
    }

    [Fact]
    public void Section_runs_to_end_of_document_when_no_following_sibling()
    {
        var s = _ex.Extract(Doc, "Next");
        Assert.NotNull(s);
        string body = Doc.Substring(s!.Start, s.End - s.Start);
        Assert.Contains("gamma", body);
        Assert.Equal(Doc.Length, s.End);
    }

    [Fact]
    public void Heading_match_is_case_and_whitespace_insensitive()
    {
        Assert.NotNull(_ex.Extract(Doc, "  intro  "));
    }

    [Fact]
    public void Missing_heading_returns_null()
    {
        Assert.Null(_ex.Extract(Doc, "Nonexistent"));
    }
}
