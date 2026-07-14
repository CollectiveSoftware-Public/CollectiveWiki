// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Models;
using Wiki.Core.Vault;

namespace Wiki.Core.Indexing;

/// <summary>The links/backlinks/tags/FTS index over a vault. Rebuildable cache (notes are the source of
/// truth); <see cref="Apply"/> keeps it current incrementally from <see cref="VaultChange"/> events.</summary>
public interface IWikiIndex
{
    void Rebuild();
    void Apply(VaultChange change);
    IReadOnlyList<string> AllNotes();
    IReadOnlyList<WikiLink> LinksOf(string notePath);
    IReadOnlyList<Backlink> BacklinksOf(string noteTargetPath);
    IReadOnlyList<string> NotesWithTag(string tag);

    /// <summary>Every distinct tag in the vault, case-insensitively de-duplicated and sorted — the tag
    /// browser lists these.</summary>
    IReadOnlyList<string> AllTags();

    IReadOnlyList<SearchHit> Search(string query, int limit = 50);
}
