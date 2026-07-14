// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Search;

/// <summary>One matching line of a note in the vault-wide search pane. <see cref="Line"/> is 0-based;
/// <see cref="MatchStart"/>/<see cref="MatchLength"/> locate the query within <see cref="Text"/> so the UI
/// can bold it.</summary>
public sealed record SearchSnippet(int Line, string Text, int MatchStart, int MatchLength);

/// <summary>A note that matched a vault-wide search, with up to N line snippets. Ordered by the index's
/// relevance score (the caller preserves that order).</summary>
public sealed record VaultSearchResult(string NotePath, string Title, IReadOnlyList<SearchSnippet> Snippets);
