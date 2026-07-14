// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Desktop.Sync;

/// <summary>Recognizes the reconciler's conflicted-copy notes (`Name (conflicted copy, &lt;device&gt; &lt;date&gt;).md`)
/// so the tree can badge them (spec §10 — surface only; an in-app merge view is a follow-up).</summary>
public static class ConflictCopy
{
    public static bool IsConflictNote(string relativePath)
        => relativePath.Contains("(conflicted copy", StringComparison.Ordinal);

    /// <summary>How many of <paramref name="notePaths"/> are conflicted copies — the status-bar counter.</summary>
    public static int Count(IEnumerable<string> notePaths) => notePaths.Count(IsConflictNote);
}
