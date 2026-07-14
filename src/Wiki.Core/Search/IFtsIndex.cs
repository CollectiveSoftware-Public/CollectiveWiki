// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Models;

namespace Wiki.Core.Search;

/// <summary>Full-text search over note content. A rebuildable cache: notes are the source of truth.
/// v1 impl is in-memory + pure; a SQLite/FTS5-backed impl in the .cwiki sidecar lands in Phase 1
/// behind this same seam.</summary>
public interface IFtsIndex
{
    void Add(string notePath, string content);
    void Update(string notePath, string content);
    void Remove(string notePath);
    IReadOnlyList<SearchHit> Search(string query, int limit = 50);
}
