// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Embedding;

/// <summary>A heading and the absolute source range of its section (the heading line through the text
/// just before the next same-or-higher-level heading, or end of document). <c>End</c> is exclusive.</summary>
public sealed record NoteSection(string Heading, int Level, int Start, int End);
