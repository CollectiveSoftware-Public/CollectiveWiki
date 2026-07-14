// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Sync;

/// <summary>The result of attempting a 3-way text merge: the merged text plus whether any region
/// could not be auto-resolved (the reconciler falls back to a conflicted copy when true).</summary>
public sealed record MergeAttempt(string MergedText, bool HasConflicts);

/// <summary>Three-way text merge seam. v1 concrete adapts CollectiveDiff's line-based Diff3Merger;
/// a CRDT/character merge could replace it later without changing the reconciler.</summary>
public interface IThreeWayMerger
{
    MergeAttempt Merge(string @base, string left, string right);
}
