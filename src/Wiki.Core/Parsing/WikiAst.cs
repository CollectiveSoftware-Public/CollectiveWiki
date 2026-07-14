// SPDX-License-Identifier: GPL-3.0-or-later
using Markdig.Syntax;
using Wiki.Core.Models;

namespace Wiki.Core.Parsing;

/// <summary>The single parse of one note: the Markdig block AST plus the wiki-specific syntax
/// (links/embeds, tags, frontmatter) extracted from it. One parse feeds the index AND the editor —
/// never two divergent parsers.</summary>
public sealed record WikiAst(
    MarkdownDocument Document,
    IReadOnlyList<WikiLink> Links,
    IReadOnlyList<TagRef> Tags,
    IReadOnlyDictionary<string, string> Frontmatter);
