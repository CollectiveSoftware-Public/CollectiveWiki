// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wiki.Core.Search;
using Wiki.Core.Vault;

namespace Wiki.Storage.DependencyInjection;

/// <summary>Opts a host into the persistent SQLite/FTS5 index (in the vault's .cwiki sidecar) in place of
/// the in-memory default. Call AFTER AddWikiCore/AddWikiVault so this registration wins.</summary>
public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddWikiSqliteFts(this IServiceCollection services, string vaultRoot)
    {
        string dbPath = CwikiPaths.IndexDbPath(vaultRoot);
        Directory.CreateDirectory(CwikiPaths.SidecarDir(vaultRoot));
        services.RemoveAll<IFtsIndex>();
        services.AddSingleton<IFtsIndex>(_ => new SqliteFtsIndex(dbPath));
        return services;
    }
}
