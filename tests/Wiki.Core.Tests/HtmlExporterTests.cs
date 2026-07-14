// SPDX-License-Identifier: GPL-3.0-or-later
using System.Linq;
using Wiki.Core.Export;
using Xunit;

namespace Wiki.Core.Tests;

public class HtmlExporterTests
{
    [Fact]
    public void RenderNote_IsSelfContainedAndRewritesWikiSyntax()
    {
        string html = HtmlExporter.RenderNote(
            "# Hi\n\nSee [[Other|there]] and #kb\n\n![[pic.png]]\n",
            "Hi",
            asset => asset == "pic.png" ? "data:image/png;base64,AAAA" : null,
            target => target + ".html");
        Assert.Contains("<title>Hi</title>", html);
        Assert.Contains("<style>", html);                       // inline CSS → self-contained
        Assert.Contains("href=\"Other.html\"", html);           // wikilink → note href
        Assert.Contains(">there<", html);                       // alias used
        Assert.Contains("<span class=\"tag\">#kb</span>", html);
        Assert.Contains("src=\"data:image/png;base64,AAAA\"", html);
    }

    [Fact]
    public void RenderNote_DropsUnresolvedImage()
    {
        string html = HtmlExporter.RenderNote("![[missing.png]]\n", "T", _ => null, t => t + ".html");
        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public void RenderVault_LinksNotesAndAddsIndex()
    {
        var notes = new[] { ("a/One.md", "Link to [[Two]]\n"), ("Two.md", "hi\n") };
        var files = HtmlExporter.RenderVault(notes, _ => null);
        Assert.Contains(files, f => f.RelPath == "index.html" && f.Html.Contains("One") && f.Html.Contains("Two"));
        var one = files.Single(f => f.RelPath == "a/One.html");
        Assert.Contains("href=\"Two.html\"", one.Html);
    }
}
