// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Models;

/// <summary>A parsed <c>[[Target#Heading|Alias]]</c> (or <c>![[...]]</c> embed) with its absolute
/// source range in the note text (<paramref name="SourceEnd"/> exclusive).</summary>
public sealed record WikiLink(
    string Target, string? Heading, string? Alias, bool IsEmbed, int SourceStart, int SourceEnd)
{
    /// <summary>What the reader sees: the alias if present, otherwise the target.</summary>
    public string DisplayText => Alias ?? Target;
}
