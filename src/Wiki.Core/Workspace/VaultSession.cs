// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Indexing;
using Wiki.Core.Models;
using Wiki.Core.Search;
using Wiki.Core.Templating;
using Wiki.Core.Vault;

namespace Wiki.Core.Workspace;

/// <summary>A facade over an open vault: the file system + index + resolver composed for the head.
/// UI-free and unit-tested; the desktop's MainViewModel is a thin wrapper over this.</summary>
public sealed class VaultSession
{
    private readonly IVaultFileSystem _files;
    private readonly IWikiIndex _index;
    private readonly ILinkResolver _resolver;
    private readonly ITemplateEngine _templates = new TemplateEngine();   // pure/stateless — owned, not injected
    private readonly BookmarkStore _bookmarks;
    private Dictionary<string, List<string>>? _assetMap;   // file name (lowercased) -> '/'-relative paths

    public VaultSession(IVaultFileSystem files, IWikiIndex index, ILinkResolver resolver)
    {
        _files = files;
        _index = index;
        _resolver = resolver;
        _bookmarks = new BookmarkStore(files);
    }

    /// <summary>The vault's bookmarked notes (persisted to <c>.cwiki/bookmarks.json</c>). Kept path-consistent
    /// here: a note rename/move updates its bookmark, a delete drops it.</summary>
    public BookmarkStore Bookmarks => _bookmarks;

    public IReadOnlyList<string> Notes() => _files.EnumerateMarkdownFiles();

    public string Read(string notePath) => _files.ReadAllText(notePath);

    public void Save(string notePath, string content)
    {
        bool existed = _files.Exists(notePath);
        _files.WriteAllText(notePath, content);
        _index.Apply(new VaultChange(existed ? VaultChangeKind.Modified : VaultChangeKind.Added, notePath, null));
    }

    public IReadOnlyList<Backlink> Backlinks(string notePath) => _index.BacklinksOf(notePath);

    /// <summary>Full-text search over the vault index (tf·idf + title ranking). The quick switcher's
    /// content tier reaches the already-built index through this passthrough.</summary>
    public IReadOnlyList<SearchHit> Search(string query, int limit = 50) => _index.Search(query, limit);

    /// <summary>Vault-wide full-text search with per-note line snippets, for the Search pane. Runs the FTS
    /// index, then reads each hit once to build snippets. The index's score order is preserved.</summary>
    public IReadOnlyList<VaultSearchResult> SearchWithSnippets(string query, int limit = 50)
    {
        var results = new List<VaultSearchResult>();
        if (string.IsNullOrWhiteSpace(query)) return results;
        var q = SearchQuery.Parse(query);

        // Candidate note paths: FTS on the plain terms when present, else (filters only) the whole vault.
        IEnumerable<string> candidates =
            q.Terms.Count > 0 ? _index.Search(q.TermQuery, limit * 4).Select(h => h.NotePath)
            : q.HasFilters ? _files.EnumerateMarkdownFiles()
            : Array.Empty<string>();

        // Precompute the note set allowed by the tag filters (prefix-aware: tag:area matches area/sub).
        HashSet<string>? tagAllowed = null;
        foreach (var t in q.Tags)
        {
            var set = NotesWithTagPrefix(t).ToHashSet(StringComparer.Ordinal);
            tagAllowed = tagAllowed is null ? set : tagAllowed.Intersect(set).ToHashSet(StringComparer.Ordinal);
        }

        string highlight = q.Phrases.FirstOrDefault() ?? q.Terms.FirstOrDefault()
            ?? (q.Tags.Count > 0 ? "#" + q.Tags[0] : "");

        foreach (var path in candidates)
        {
            if (tagAllowed is not null && !tagAllowed.Contains(path)) continue;
            if (q.Paths.Any(p => path.IndexOf(p, StringComparison.OrdinalIgnoreCase) < 0)) continue;
            string text = q.Phrases.Count > 0 ? Read(path) : "";
            if (q.Phrases.Any(ph => text.IndexOf(ph, StringComparison.OrdinalIgnoreCase) < 0)) continue;
            results.Add(new VaultSearchResult(path, System.IO.Path.GetFileNameWithoutExtension(path),
                SnippetBuilder.Build(q.Phrases.Count > 0 ? text : Read(path), highlight)));
            if (results.Count >= limit) break;
        }
        return results;
    }

    // Notes carrying a tag or any nested child of it (tag:area → area, area/sub, …).
    private IEnumerable<string> NotesWithTagPrefix(string tag)
    {
        foreach (var t in _index.AllTags())
            if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)
                || t.StartsWith(tag + "/", StringComparison.OrdinalIgnoreCase))
                foreach (var n in _index.NotesWithTag(t)) yield return n;
    }

    /// <summary>Every distinct tag in the vault (sorted, case-insensitive) — the tag browser lists these.</summary>
    public IReadOnlyList<string> AllTags() => _index.AllTags();

    /// <summary>Renames a tag (and its nested children) across every note that carries it, re-saving (and thus
    /// re-indexing) each changed note. Returns the changed note paths. Mirrors <see cref="RenameNote"/>'s
    /// compute→write→reindex flow.</summary>
    public IReadOnlyList<string> RenameTag(string oldTag, string newTag)
    {
        var changed = new List<string>();
        if (string.IsNullOrWhiteSpace(oldTag) || string.IsNullOrWhiteSpace(newTag)) return changed;
        foreach (var path in _files.EnumerateMarkdownFiles())
        {
            string text = Read(path);
            string rewritten = Wiki.Core.Indexing.TagRename.Rewrite(text, oldTag.Trim(), newTag.Trim());
            if (rewritten != text) { Save(path, rewritten); changed.Add(path); }
        }
        return changed;
    }

    /// <summary>The notes carrying <paramref name="tag"/> (without the leading '#').</summary>
    public IReadOnlyList<string> NotesWithTag(string tag) => _index.NotesWithTag(tag);

    /// <summary>Creates an empty note titled <paramref name="title"/> in <paramref name="folder"/> ("" = the
    /// vault root), disambiguating the file name if one already exists. Returns the new '/'-relative path.</summary>
    public string CreateNote(string title, string folder = "")
    {
        string safe = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim();
        string dir = string.IsNullOrEmpty(folder) ? "" : folder.TrimEnd('/') + "/";
        string path = dir + safe + ".md";
        int n = 1;
        while (_files.Exists(path)) path = $"{dir}{safe} {++n}.md";
        Save(path, $"# {safe}\n");
        return path;
    }

    /// <summary>Creates a folder named <paramref name="name"/> under <paramref name="parentFolder"/> ("" =
    /// root), disambiguating on collision. Returns the new '/'-relative folder path.</summary>
    public string CreateFolder(string parentFolder, string name)
    {
        string parent = string.IsNullOrEmpty(parentFolder) ? "" : parentFolder.TrimEnd('/') + "/";
        string trimmed = name.Trim();
        string path = parent + trimmed;
        int n = 1;
        while (FolderExists(path) || _files.Exists(path)) path = $"{parent}{trimmed} {++n}";
        _files.CreateDirectory(path);
        return path;
    }

    /// <summary>All '/'-relative folders in the vault (for the tree + folder pickers).</summary>
    public IReadOnlyList<string> Folders() => _files.EnumerateFolders();

    /// <summary>Builds the vault's link graph (notes as nodes, resolved wikilinks as undirected edges) for the
    /// graph view. O(vault); the head calls it off the UI thread.</summary>
    public Wiki.Core.Graph.IGraphModel BuildGraph()
        => Wiki.Core.Graph.GraphModel.Build(_index, _resolver);

    private bool FolderExists(string folder) => _files.EnumerateFolders().Contains(folder);

    /// <summary>The names (without <c>.md</c>) of the note templates in <see cref="TemplatesFolder"/>.</summary>
    public IReadOnlyList<string> ListTemplates()
    {
        string prefix = TemplatesFolder.Trim().Trim('/') + "/";
        return _files.EnumerateMarkdownFiles()
            .Where(p => p.StartsWith(prefix, StringComparison.Ordinal))
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Renders template <paramref name="name"/> for a note titled <paramref name="title"/>,
    /// substituting <c>{{title}}</c>/<c>{{date}}</c>/<c>{{time}}</c>. Returns "" when the template is absent.</summary>
    public string RenderTemplate(string name, string title)
    {
        string path = TemplatesFolder.Trim().Trim('/') + "/" + name + ".md";
        return _files.Exists(path)
            ? _templates.Render(_files.ReadAllText(path), new TemplateContext(title, DateTimeOffset.Now))
            : "";
    }

    /// <summary>Creates a note titled <paramref name="title"/> in <paramref name="folder"/> ("" = root) from
    /// template <paramref name="name"/> (disambiguated on collision). Returns the new note path.</summary>
    public string CreateFromTemplate(string name, string title, string folder = "")
    {
        string safe = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim();
        string dir = string.IsNullOrEmpty(folder) ? "" : folder.TrimEnd('/') + "/";
        string path = dir + safe + ".md";
        int n = 1;
        while (_files.Exists(path)) path = $"{dir}{safe} {++n}.md";
        Save(path, RenderTemplate(name, safe));
        return path;
    }

    /// <summary>Renames a note to <paramref name="newTitle"/> (same folder), rewriting every inbound
    /// wikilink so links keep resolving, and keeping the index consistent. Returns the new '/'-relative
    /// path. A blank title throws; renaming to the current name is a no-op; a name collision is
    /// disambiguated with <c>" 2"</c>, <c>" 3"</c>, … (the <see cref="CreateNote"/> idiom).</summary>
    public string RenameNote(string notePath, string newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle)) throw new ArgumentException("New note name must not be blank.", nameof(newTitle));
        string safe = newTitle.Trim();

        int slash = notePath.LastIndexOf('/');
        string folder = slash >= 0 ? notePath[..(slash + 1)] : "";   // keeps the trailing '/'

        string newPath = folder + safe + ".md";
        if (newPath == notePath) return notePath;                    // unchanged — nothing to do
        int n = 1;
        while (_files.Exists(newPath)) newPath = $"{folder}{safe} {++n}.md";
        return MoveNoteInternal(notePath, newPath);
    }

    /// <summary>Moves a note to <paramref name="newFolder"/> ("" = root), keeping its file name (disambiguated
    /// on collision) and rewriting inbound links. Returns the new '/'-relative path (== the old path if it is
    /// already there).</summary>
    public string MoveNote(string notePath, string newFolder)
    {
        int slash = notePath.LastIndexOf('/');
        string fileName = slash >= 0 ? notePath[(slash + 1)..] : notePath;
        string baseName = fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? fileName[..^3] : fileName;
        string dir = string.IsNullOrEmpty(newFolder) ? "" : newFolder.TrimEnd('/') + "/";
        string newPath = dir + fileName;
        if (newPath == notePath) return notePath;
        int n = 1;
        while (_files.Exists(newPath)) newPath = $"{dir}{baseName} {++n}.md";
        return MoveNoteInternal(notePath, newPath);
    }

    /// <summary>Renames a folder in place (same parent), moving every note under it (links preserved).
    /// Returns the new folder path.</summary>
    public string RenameFolder(string folderPath, string newName)
    {
        string trimmed = folderPath.TrimEnd('/');
        int slash = trimmed.LastIndexOf('/');
        string parent = slash >= 0 ? trimmed[..slash] : "";
        return MoveFolderTo(trimmed, parent, newName.Trim());
    }

    /// <summary>Moves a folder (and everything under it) into <paramref name="newParent"/> ("" = root),
    /// preserving inbound links. Returns the new folder path; a no-op when moving into its own subtree.</summary>
    public string MoveFolder(string folderPath, string newParent)
    {
        string trimmed = folderPath.TrimEnd('/');
        int slash = trimmed.LastIndexOf('/');
        string name = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        return MoveFolderTo(trimmed, newParent.TrimEnd('/'), name);
    }

    private string MoveFolderTo(string oldFolder, string newParent, string newName)
    {
        // Refuse to move a folder into itself or a descendant (would recurse forever).
        if (newParent == oldFolder || newParent.StartsWith(oldFolder + "/", StringComparison.Ordinal))
            return oldFolder;

        string parent = string.IsNullOrEmpty(newParent) ? "" : newParent + "/";
        string newFolder = parent + newName;
        if (newFolder == oldFolder) return oldFolder;
        int n = 1;
        while (FolderExists(newFolder) || _files.Exists(newFolder)) newFolder = $"{parent}{newName} {++n}";

        _files.CreateDirectory(newFolder);
        string prefix = oldFolder + "/";
        foreach (var note in _files.EnumerateMarkdownFiles()
                     .Where(p => p.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            string rel = note[prefix.Length..];                    // path within the folder (may nest)
            MoveNoteInternal(note, newFolder + "/" + rel);
        }
        _files.DeleteDirectory(oldFolder);
        return newFolder;
    }

    // Moves a note's file from -> to, rewriting every inbound wikilink (computed BEFORE the move: BacklinksOf
    // resolves live, so once the old note is gone its inbound links no longer resolve) and moving the note's
    // own index entry. Shared by RenameNote / MoveNote / folder moves.
    private string MoveNoteInternal(string fromPath, string toPath)
    {
        var rewrites = _resolver.ComputeRenameRewrites(_index, fromPath, toPath);
        _files.Rename(fromPath, toPath);
        _bookmarks.Rename(fromPath, toPath);

        // Apply each source note's edits back-to-front (descending offset) so earlier offsets stay valid,
        // re-saving each source (which re-indexes it). Skip a source that no longer exists (e.g. a self-link
        // whose source note is the one just moved).
        foreach (var group in rewrites.GroupBy(r => r.NotePath))
        {
            if (!_files.Exists(group.Key)) continue;
            string text = _files.ReadAllText(group.Key);
            foreach (var rw in group.OrderByDescending(r => r.SourceStart))
                text = text[..rw.SourceStart] + rw.NewLinkText + text[rw.SourceEnd..];
            Save(group.Key, text);
        }

        _index.Apply(new VaultChange(VaultChangeKind.Renamed, toPath, fromPath));
        _assetMap = null;
        return toPath;
    }

    /// <summary>Deletes a note and drops it from the index. A no-op (never throws) when the note is
    /// already gone.</summary>
    public void DeleteNote(string notePath)
    {
        _files.Delete(notePath);
        _index.Apply(new VaultChange(VaultChangeKind.Deleted, notePath, null));
        _bookmarks.Remove(notePath);
        _assetMap = null;
    }

    /// <summary>Deletes a folder and every note under it (each dropped from the index), then removes the
    /// directory (with any remaining non-note files).</summary>
    public void DeleteFolder(string folderPath)
    {
        string prefix = folderPath.TrimEnd('/') + "/";
        foreach (var note in _files.EnumerateMarkdownFiles()
                     .Where(p => p.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            DeleteNote(note);
        _files.DeleteDirectory(folderPath);
        _assetMap = null;
    }

    /// <summary>The folder pasted image assets are written to (the vault's attachments folder). The head
    /// sets this from settings; defaults to <c>attachments</c>.</summary>
    public string AttachmentsFolder { get; set; } = "attachments";

    /// <summary>The folder note templates are read from. The head sets this from settings; defaults to
    /// <c>templates</c>.</summary>
    public string TemplatesFolder { get; set; } = "templates";

    /// <summary>Saves a pasted image into the vault's attachments folder as
    /// <c>Pasted image &lt;timestamp&gt;.&lt;ext&gt;</c> (disambiguated on collision) and returns the embed
    /// target — the bare file name, so the caller inserts <c>![[Pasted image ….png]]</c>, which resolves by
    /// basename via <see cref="ResolveAssetPath"/> even though the file lives in a subfolder.</summary>
    public string SaveAsset(byte[] data, string extension)
    {
        string folder = string.IsNullOrWhiteSpace(AttachmentsFolder) ? "attachments" : AttachmentsFolder.Trim().Trim('/');
        string ext = string.IsNullOrWhiteSpace(extension) ? "png" : extension.Trim().TrimStart('.').ToLowerInvariant();
        string stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        string name = $"Pasted image {stamp}.{ext}";
        int n = 1;
        while (_files.Exists($"{folder}/{name}"))
            name = $"Pasted image {stamp}-{++n}.{ext}";
        _files.WriteAllBytes($"{folder}/{name}", data);
        _assetMap = null;   // a new asset must show up in basename resolution
        return name;
    }

    /// <summary>Resolves a wikilink target to a note path, creating an empty note when unresolved.</summary>
    public string ResolveOrCreateTarget(string target)
    {
        if (_resolver.Classify(target, out string? notePath) == LinkResolution.Resolved && notePath is not null)
            return notePath;
        return CreateNote(target);
    }

    /// <summary>Resolves an embed target (e.g. <c>image.png</c> from <c>![[image.png]]</c>) to a
    /// '/'-relative vault file, or null if not found. Tries the literal path first, then a basename match
    /// across all vault files (by-name resolution). The host joins this with the vault root
    /// to load the bitmap.</summary>
    public string? ResolveAssetPath(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        string t = target.Replace('\\', '/').Trim();
        if (t.StartsWith("./", StringComparison.Ordinal)) t = t[2..];
        else if (t.StartsWith('/')) t = t[1..];
        if (_files.Exists(t)) return t;
        return AssetMap().TryGetValue(Path.GetFileName(t).ToLowerInvariant(), out var paths) && paths.Count > 0
            ? ShortestPath(paths) : null;
    }

    /// <summary>Picks the canonical file among several sharing a basename, resolving the bare-name embed
    /// resolution: the <em>shortest path</em> wins (fewest folders, then fewest characters), ordinal as a
    /// deterministic final tiebreak. Without this, the ordinal-sorted list puts a sibling backup folder
    /// (e.g. <c>Files/Portraits.backup-…/x.png</c>) ahead of the real <c>Files/Portraits/x.png</c>, because
    /// '.' sorts before '/', so a naive first-match served a stale copy.</summary>
    private static string ShortestPath(List<string> paths)
    {
        string best = paths[0];
        for (int i = 1; i < paths.Count; i++)
            if (Compare(paths[i], best) < 0) best = paths[i];
        return best;

        static int Compare(string a, string b)
        {
            int byDepth = Depth(a) - Depth(b);
            if (byDepth != 0) return byDepth;
            int byLength = a.Length - b.Length;
            return byLength != 0 ? byLength : string.CompareOrdinal(a, b);
        }
        static int Depth(string p)
        {
            int n = 0;
            foreach (char c in p) if (c == '/') n++;
            return n;
        }
    }

    /// <summary>Pre-builds the asset index so the first image resolve doesn't walk the disk on the UI
    /// thread. Called from <see cref="VaultWorkspace.Open"/> on the background open thread.</summary>
    public void WarmAssets() => _ = AssetMap();

    private Dictionary<string, List<string>> AssetMap()
    {
        if (_assetMap is not null) return _assetMap;
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in _files.EnumerateFiles())
        {
            string key = Path.GetFileName(file).ToLowerInvariant();
            if (!map.TryGetValue(key, out var list)) map[key] = list = new();
            list.Add(file);
        }
        return _assetMap = map;
    }
}
