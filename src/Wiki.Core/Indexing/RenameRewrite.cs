// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Indexing;

/// <summary>An edit the editor must apply to keep a link valid after a note rename: in note
/// <paramref name="NotePath"/>, replace the source range [Start, End) with <paramref name="NewLinkText"/>.</summary>
public sealed record RenameRewrite(string NotePath, int SourceStart, int SourceEnd, string NewLinkText);
