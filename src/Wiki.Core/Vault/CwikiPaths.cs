// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Vault;

/// <summary>Locates the <c>.cwiki/</c> sidecar under a vault root. The sidecar holds rebuildable app
/// state (the SQLite/FTS5 index cache, later graph/sync/layout) — never note content. Notes are the
/// only source of truth.</summary>
public static class CwikiPaths
{
    public const string SidecarDirName = ".cwiki";
    public const string IndexDbFileName = "index.db";

    public static string SidecarDir(string vaultRoot) => Path.Combine(vaultRoot, SidecarDirName);
    public static string IndexDbPath(string vaultRoot) => Path.Combine(SidecarDir(vaultRoot), IndexDbFileName);
}
