// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Extensions.DependencyInjection;
using Wiki.Sync;
using Wiki.Sync.DependencyInjection;

namespace Wiki.Sync.Tests;

public class SyncDependencyInjectionTests
{
    [Fact]
    public void AddWikiSync_resolves_the_merger_and_reconciler()
    {
        var provider = new ServiceCollection().AddWikiSync().BuildServiceProvider();
        Assert.IsType<Diff3MergeAdapter>(provider.GetRequiredService<IThreeWayMerger>());
        Assert.NotNull(provider.GetRequiredService<Reconciler>());
    }

    [Fact]
    public void AddWikiSync_resolves_the_change_verifier_and_authenticating_reconciler()
    {
        var provider = new ServiceCollection().AddWikiSync().BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<ChangeVerifier>());
        Assert.NotNull(provider.GetRequiredService<AuthenticatingReconciler>());
    }

    [Fact]
    public void AddWikiSync_resolves_the_content_key_sealer()
    {
        var provider = new ServiceCollection().AddWikiSync().BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<ContentKeySealer>());
    }
}
