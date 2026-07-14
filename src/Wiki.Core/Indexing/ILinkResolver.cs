// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Indexing;

public enum LinkResolution { Resolved, Unresolved, Ambiguous }

/// <summary>Resolves a link's target to a note path and computes rename rewrites for link integrity.</summary>
public interface ILinkResolver
{
    string? Resolve(string target);
    LinkResolution Classify(string target, out string? notePath);
    IReadOnlyList<RenameRewrite> ComputeRenameRewrites(IWikiIndex index, string oldPath, string newPath);

    /// <summary>Drops any cached alias map so a subsequent resolve re-reads frontmatter. No-op when the
    /// resolver was constructed without a parser (aliases disabled).</summary>
    void InvalidateAliases();
}
