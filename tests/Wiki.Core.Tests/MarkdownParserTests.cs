// SPDX-License-Identifier: GPL-3.0-or-later
using Markdig.Syntax;
using Wiki.Core.Parsing;

namespace Wiki.Core.Tests;

public class MarkdownParserTests
{
    private readonly IMarkdownParser _parser = new MarkdigMarkdownParser();

    [Fact]
    public void Parses_headings_and_paragraphs_into_a_markdig_document()
    {
        var ast = _parser.Parse("# Title\n\nA paragraph.\n");
        Assert.NotNull(ast.Document);
        Assert.Contains(ast.Document, b => b is HeadingBlock h && h.Level == 1);
        Assert.Contains(ast.Document, b => b is ParagraphBlock);
    }

    [Fact]
    public void Empty_input_yields_an_empty_document_not_null()
    {
        var ast = _parser.Parse("");
        Assert.NotNull(ast.Document);
        Assert.Empty(ast.Links);
        Assert.Empty(ast.Tags);
    }
}
