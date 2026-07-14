// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Editor;

/// <summary>The visual style of one run of text on a rendered (non-caret) line.
/// <c>Marker</c> is a markdown syntax delimiter (<c>**</c>, <c>*</c>, <c>`</c>, <c>#</c>, <c>&gt;</c>):
/// omitted entirely on rendered lines, shown greyed on the caret's (revealed) line.</summary>
public enum RunStyle
{
    Normal, Heading, Bold, Italic, Code, Quote, ListMarker, WikiLink, Link, Rule, Image, Marker, Checkbox,
    Highlight, Strikethrough,
    CodeKeyword, CodeString, CodeComment, CodeNumber, CodeType   // fenced-code syntax tokens
}

/// <summary>A contiguous run of like-styled text produced by <see cref="RenderedLineBuilder"/> /
/// <see cref="RevealedLineBuilder"/>.
/// <paramref name="HeadingLevel"/> is 1..6 when <paramref name="Style"/> is Heading, else 0.
/// <paramref name="LinkTarget"/> carries the wikilink/link destination for click-through.</summary>
public sealed record StyledRun(string Text, RunStyle Style, int HeadingLevel = 0, string? LinkTarget = null);
