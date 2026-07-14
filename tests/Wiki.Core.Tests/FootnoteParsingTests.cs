// SPDX-License-Identifier: GPL-3.0-or-later
using System.Linq;
using Wiki.Core.Parsing;
using Xunit;

namespace Wiki.Core.Tests;

public class FootnoteParsingTests
{
    [Fact]
    public void Footnote_reference_is_surfaced_as_a_caret_target_link()
    {
        const string note = "A claim[^1].\n\n[^1]: the source\n";
        var ast = new MarkdigMarkdownParser().Parse(note);
        var link = ast.Links.FirstOrDefault(l => l.Target == "^1");
        Assert.NotNull(link);            // the reference is exposed as a link with target "^1"
        Assert.False(link!.IsEmbed);
        // Its span covers the "[^1]" reference (the first occurrence, not the definition line).
        Assert.Equal("[^1]", note.Substring(link.SourceStart, link.SourceEnd - link.SourceStart));
    }
}
