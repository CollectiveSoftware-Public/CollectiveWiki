// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Sync;

/// <summary>The per-file metadata two peers exchange in their index. <paramref name="ContentHash"/>
/// is empty for a tombstone (a propagated delete keeps its <paramref name="Version"/> so a stale peer
/// cannot resurrect the file).</summary>
public sealed record FileEntry(string Path, VersionVector Version, string ContentHash, bool Deleted)
{
    public static FileEntry Tombstone(string path, VersionVector version) => new(path, version, "", true);
}
