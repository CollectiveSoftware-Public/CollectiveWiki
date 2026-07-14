// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Models;

/// <summary>A full-text search hit: the matching note's '/'-relative path and its relevance score
/// (higher = more relevant).</summary>
public sealed record SearchHit(string NotePath, double Score);
