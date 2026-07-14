// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Graph;

/// <summary>A graph node: one note, by its '/'-relative vault path.</summary>
public sealed record GraphNode(string NotePath);

/// <summary>A directed link edge from one note to another (both resolved, existing notes).</summary>
public sealed record GraphEdge(string FromNote, string ToNote);
