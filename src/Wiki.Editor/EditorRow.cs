// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Editor;

/// <summary>What a laid-out row draws.</summary>
public enum RowKind { Text, Image, Rule, Properties, Table }

// (see EditorRow.Callout below — a Text/Rule row inside a callout block carries its CalloutInfo so the
//  surface can draw the coloured box behind the contiguous block.)

/// <summary>One laid-out row of the live-preview surface. For <see cref="RowKind.Text"/>/<c>Image</c>/
/// <c>Rule</c>, <see cref="FirstLine"/> == <see cref="LastLine"/> (a single document line). For
/// <see cref="RowKind.Properties"/> the row spans the front-matter block lines.</summary>
/// <param name="RawStart">Absolute source offset of <see cref="FirstLine"/>'s start.</param>
/// <param name="Revealed">True if this is the caret's line (raw text, markers greyed).</param>
/// <param name="Runs">Styled runs to draw (Text rows).</param>
/// <param name="DisplayToRaw">Display-char-index → absolute source offset, with a trailing line-end entry
/// (Text rows), so a click maps to a caret offset.</param>
/// <param name="ImageTarget">The embed target for <see cref="RowKind.Image"/> rows.</param>
/// <param name="Properties">Ordered key/value entries for <see cref="RowKind.Properties"/> rows.</param>
/// <param name="ImageWidth">Requested display width from a <c>|300</c>-style alias hint (Image rows).</param>
/// <param name="ImageHeight">Requested display height from a <c>|300x200</c>-style alias hint (Image rows).</param>
public sealed record EditorRow(
    RowKind Kind,
    int FirstLine,
    int LastLine,
    int RawStart,
    bool Revealed,
    IReadOnlyList<StyledRun> Runs,
    int[] DisplayToRaw,
    string? ImageTarget,
    IReadOnlyList<KeyValuePair<string, string>>? Properties,
    TableModel? Table = null,
    double? ImageWidth = null,
    double? ImageHeight = null,
    CalloutInfo? Callout = null);
