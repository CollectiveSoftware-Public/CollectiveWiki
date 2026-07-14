// SPDX-License-Identifier: GPL-3.0-or-later
using Diff.Core.Diff;
using Diff.Core.Merge;

namespace Wiki.Sync;

/// <summary>Adapts CollectiveDiff's packaged <see cref="Diff3Merger"/> to <see cref="IThreeWayMerger"/>.
/// Text is normalised to LF and split into lines; conflicts are reported via the diff3 result so the
/// reconciler can choose a conflicted copy rather than writing diff3 markers into a note.</summary>
public sealed class Diff3MergeAdapter : IThreeWayMerger
{
    private static readonly DiffOptions Options = DiffOptions.Default;
    private readonly IMerger _merger;

    public Diff3MergeAdapter() : this(new Diff3Merger(new LineDiffEngine())) { }

    public Diff3MergeAdapter(IMerger merger) => _merger = merger;

    public MergeAttempt Merge(string @base, string left, string right)
    {
        var result = _merger.Merge(Lines(@base), Lines(left), Lines(right), Options);
        return new MergeAttempt(string.Join("\n", result.MergedLines), result.HasConflicts);
    }

    private static string[] Lines(string text) => text.Replace("\r\n", "\n").Split('\n');
}
