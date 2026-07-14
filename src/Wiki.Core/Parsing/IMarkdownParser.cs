// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Parsing;

/// <summary>Parses note markdown into a <see cref="WikiAst"/>. The one parser in the system.</summary>
public interface IMarkdownParser
{
    WikiAst Parse(string text);
}
