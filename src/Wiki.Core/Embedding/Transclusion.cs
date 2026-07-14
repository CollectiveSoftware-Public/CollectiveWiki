// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Embedding;

public enum TransclusionKind { Note, Section, Image, Unresolved }

/// <summary>The result of resolving an <c>![[...]]</c> embed. For <see cref="TransclusionKind.Note"/>/
/// <see cref="TransclusionKind.Section"/>, <paramref name="Content"/> is the text to inline and
/// <paramref name="ResolvedPath"/> the source note. For <see cref="TransclusionKind.Image"/>,
/// <paramref name="ResolvedPath"/> is the image target and <paramref name="Content"/> is null.</summary>
public sealed record Transclusion(TransclusionKind Kind, string? ResolvedPath, string? Content);
