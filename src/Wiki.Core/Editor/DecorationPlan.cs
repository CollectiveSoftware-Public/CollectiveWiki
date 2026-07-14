// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Editor;

public enum SpanKind
{
    Plain, Heading1, Heading2, Heading3, Heading4, Heading5, Heading6,
    Bold, Italic, InlineCode, CodeBlock, ListItem, Quote, Link, WikiLink, Embed, HorizontalRule,
    Highlight, Strikethrough
}

/// <summary>A styled range within the note, [Start, End) absolute offsets.</summary>
public sealed record DecorationSpan(int Start, int End, SpanKind Kind);

/// <summary>The plan for one document line: whether to reveal raw source (caret is on it) and the
/// styled spans that intersect it.</summary>
public sealed record LineDecoration(int LineIndex, bool RevealSource, IReadOnlyList<DecorationSpan> Spans);

/// <summary>A block-level widget anchor (image/transclusion). Filled in Task 12.</summary>
public sealed record WidgetAnchor(int Offset, WidgetKind Kind, string Target);
public enum WidgetKind { Image, Transclusion }

/// <summary>The complete render plan for a note + selection: per-line decorations and block widgets.</summary>
public sealed record DecorationPlan(IReadOnlyList<LineDecoration> Lines, IReadOnlyList<WidgetAnchor> Widgets);
