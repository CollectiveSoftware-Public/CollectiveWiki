// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Models;
using Wiki.Core.Parsing;
using Wiki.Core.Search;
using Wiki.Core.Vault;

namespace Wiki.Core.Indexing;

/// <summary>In-memory links/backlinks/tags index plus an <see cref="IFtsIndex"/>. One parse per note via
/// <see cref="IMarkdownParser"/>; backlinks are derived on demand from the live link map (always consistent
/// after incremental edits). <see cref="Apply"/> updates only the touched note — no full rebuild.</summary>
public sealed class WikiIndex : IWikiIndex
{
    private readonly IVaultFileSystem _files;
    private readonly IMarkdownParser _parser;
    private readonly ILinkResolver _resolver;
    private readonly IFtsIndex _fts;

    private readonly HashSet<string> _notes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<WikiLink>> _links = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _tags = new(StringComparer.Ordinal);

    public WikiIndex(IVaultFileSystem files, IMarkdownParser parser, ILinkResolver resolver, IFtsIndex fts)
        => (_files, _parser, _resolver, _fts) = (files, parser, resolver, fts);

    public void Rebuild()
    {
        _notes.Clear(); _links.Clear(); _tags.Clear();
        foreach (var note in _files.EnumerateMarkdownFiles())
            IndexNote(note, _files.ReadAllText(note));
        _resolver.InvalidateAliases();
    }

    public void Apply(VaultChange change)
    {
        switch (change.Kind)
        {
            case VaultChangeKind.Added:
            case VaultChangeKind.Modified:
                IndexNote(change.Path, _files.ReadAllText(change.Path));
                break;
            case VaultChangeKind.Deleted:
                RemoveNote(change.Path);
                break;
            case VaultChangeKind.Renamed:
                if (change.OldPath is { } old) RemoveNote(old);
                IndexNote(change.Path, _files.ReadAllText(change.Path));
                break;
        }
        _resolver.InvalidateAliases();
    }

    private void IndexNote(string note, string content)
    {
        RemoveNote(note);                       // idempotent re-index
        _notes.Add(note);
        var ast = _parser.Parse(content);
        _links[note] = ast.Links.ToList();
        _tags[note] = ast.Tags.Select(t => t.Name).ToList();
        _fts.Update(note, content);
    }

    private void RemoveNote(string note)
    {
        _notes.Remove(note);
        _links.Remove(note);
        _tags.Remove(note);
        _fts.Remove(note);
    }

    public IReadOnlyList<string> AllNotes()
        => _notes.OrderBy(p => p, StringComparer.Ordinal).ToList();

    public IReadOnlyList<WikiLink> LinksOf(string notePath)
        => _links.TryGetValue(notePath, out var l) ? l : Array.Empty<WikiLink>();

    // Backlinks are derived on demand from the live link map (always consistent after incremental edits).
    public IReadOnlyList<Backlink> BacklinksOf(string noteTargetPath)
    {
        var result = new List<Backlink>();
        foreach (var (from, links) in _links)
            foreach (var link in links)
                if (_resolver.Resolve(link.Target) == noteTargetPath)
                    result.Add(new Backlink(from, link));
        return result;
    }

    public IReadOnlyList<string> NotesWithTag(string tag)
        => _tags.Where(kv => kv.Value.Contains(tag))
                .Select(kv => kv.Key).OrderBy(p => p, StringComparer.Ordinal).ToList();

    public IReadOnlyList<string> AllTags()
        => _tags.Values.SelectMany(t => t)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<SearchHit> Search(string query, int limit = 50) => _fts.Search(query, limit);
}
