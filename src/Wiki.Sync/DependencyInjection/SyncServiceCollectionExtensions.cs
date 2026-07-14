// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Wiki.Sync.DependencyInjection;

/// <summary>Registers the stateless P2P-sync reconcile + authorization services. The identity-bearing
/// objects (<see cref="P2pSyncEngine"/>, <see cref="ChangeSigner"/>, <see cref="DeviceIdentity"/>) are
/// constructed by the head with a runtime device identity + content source (a later plan).</summary>
public static class SyncServiceCollectionExtensions
{
    public static IServiceCollection AddWikiSync(this IServiceCollection services)
    {
        services.TryAddSingleton<IThreeWayMerger, Diff3MergeAdapter>();
        services.TryAddSingleton<Reconciler>();
        services.TryAddSingleton<ChangeVerifier>();
        services.TryAddSingleton<AuthenticatingReconciler>();
        services.TryAddSingleton<ContentKeySealer>();
        return services;
    }
}
