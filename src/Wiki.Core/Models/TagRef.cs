// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Models;

/// <summary>A parsed <c>#tag</c> (or nested <c>#a/b</c>) with its absolute source range (End exclusive).</summary>
public sealed record TagRef(string Name, int SourceStart, int SourceEnd);
