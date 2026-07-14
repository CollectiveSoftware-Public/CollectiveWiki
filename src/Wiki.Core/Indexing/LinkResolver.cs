// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Parsing;
using Wiki.Core.Vault;

namespace Wiki.Core.Indexing;

public sealed class LinkResolver : ILinkResolver
{
    private readonly IVaultFileSystem _files;
    private readonly IMarkdownParser? _parser;
    private Dictionary<string, List<string>>? _aliasMap;       // alias (lowercased) -> note paths
    private Dictionary<string, List<string>>? _basenameMap;    // file name (lowercased) -> note paths

    public LinkResolver(IVaultFileSystem files, IMarkdownParser? parser = null)
        => (_files, _parser) = (files, parser);

    public string? Resolve(string target) => Classify(target, out var p) == LinkResolution.Resolved ? p : null;

    public LinkResolution Classify(string target, out string? notePath)
    {
        notePath = null;
        string t = Normalize(target);
        string normalized = t.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? t : t + ".md";

        if (_files.Exists(normalized)) { notePath = normalized; return LinkResolution.Resolved; }

        // Basename match via the cached index (O(1)) instead of rescanning the whole vault per call —
        // BacklinksOf resolves every link in the vault, so the old per-call scan froze big vaults.
        var matches = BasenameMap().TryGetValue(Path.GetFileName(normalized).ToLowerInvariant(), out var bm)
            ? bm : null;
        if (matches is { Count: 1 }) { notePath = matches[0]; return LinkResolution.Resolved; }
        if (matches is { Count: > 1 }) return LinkResolution.Ambiguous;

        // No path/basename match — try frontmatter aliases (only when a parser was supplied).
        if (_parser is not null)
        {
            var byAlias = AliasMap().TryGetValue(t.ToLowerInvariant(), out var paths) ? paths : null;
            if (byAlias is { Count: 1 }) { notePath = byAlias[0]; return LinkResolution.Resolved; }
            if (byAlias is { Count: > 1 }) return LinkResolution.Ambiguous;
        }
        return LinkResolution.Unresolved;
    }

    public IReadOnlyList<RenameRewrite> ComputeRenameRewrites(IWikiIndex index, string oldPath, string newPath)
    {
        string newTarget = StripMdExtension(Path.GetFileName(newPath));
        var rewrites = new List<RenameRewrite>();
        foreach (var back in index.BacklinksOf(oldPath))
        {
            var link = back.Link;
            string inner = newTarget
                + (link.Heading is null ? "" : "#" + link.Heading)
                + (link.Alias is null ? "" : "|" + link.Alias);
            string newText = (link.IsEmbed ? "![[" : "[[") + inner + "]]";
            rewrites.Add(new RenameRewrite(back.FromNote, link.SourceStart, link.SourceEnd, newText));
        }
        return rewrites;
    }

    public void InvalidateAliases() { _aliasMap = null; _basenameMap = null; }

    // file name (lowercased) -> all note paths with that name; built once, cleared by InvalidateAliases.
    private Dictionary<string, List<string>> BasenameMap()
    {
        if (_basenameMap is not null) return _basenameMap;
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var note in _files.EnumerateMarkdownFiles())
        {
            string key = Path.GetFileName(note).ToLowerInvariant();
            if (!map.TryGetValue(key, out var list)) map[key] = list = new();
            list.Add(note);
        }
        return _basenameMap = map;
    }

    private Dictionary<string, List<string>> AliasMap()
    {
        if (_aliasMap is not null) return _aliasMap;
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var note in _files.EnumerateMarkdownFiles())
        {
            var fm = _parser!.Parse(_files.ReadAllText(note)).Frontmatter;
            if (!fm.TryGetValue("aliases", out var raw)) continue;
            foreach (var piece in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string key = piece.ToLowerInvariant();
                if (!map.TryGetValue(key, out var list)) map[key] = list = new();
                if (!list.Contains(note)) list.Add(note);
            }
        }
        return _aliasMap = map;
    }

    // Accept Windows-style and dot-relative link text: '\' -> '/', drop a leading './' or '/'.
    private static string Normalize(string target)
    {
        string t = target.Replace('\\', '/').Trim();
        if (t.StartsWith("./", StringComparison.Ordinal)) t = t[2..];
        else if (t.StartsWith('/')) t = t[1..];
        return t;
    }

    private static string StripMdExtension(string fileName)
        => fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? fileName[..^3] : fileName;
}
