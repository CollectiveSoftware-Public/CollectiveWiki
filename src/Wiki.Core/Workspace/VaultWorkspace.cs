// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Extensions.DependencyInjection;
using Wiki.Core.DependencyInjection;
using Wiki.Core.Indexing;
using Wiki.Core.Journal;
using Wiki.Core.Vault;

namespace Wiki.Core.Workspace;

/// <summary>Opens a vault: wires the vault-scoped services, rebuilds the index, and composes the
/// <see cref="VaultSession"/>. This is the heavy part of opening a vault (a large vault rebuilds
/// thousands of notes) — kept UI-free and synchronous so the desktop head can run it off the UI
/// thread (<c>Task.Run</c>) and never freeze the window. Pure orchestration; unit-tested.</summary>
public static class VaultWorkspace
{
    /// <summary>The composed result of opening a vault: the session facade, the daily-notes service,
    /// and the current note list (captured once so the caller needn't re-enumerate).</summary>
    public sealed record OpenResult(VaultSession Session, IDailyNotes DailyNotes, IReadOnlyList<string> Notes);

    /// <summary>Builds the index/session for the vault rooted at <paramref name="root"/>. Blocking —
    /// call inside <c>Task.Run</c> from a UI thread.</summary>
    public static OpenResult Open(string root)
    {
        var services = new ServiceCollection();
        services.AddWikiCore();
        services.AddWikiVault(root);
        // Not disposed: the returned object graph (session -> index -> fts, daily) keeps the singletons
        // alive; disposing here would tear them out from under the caller.
        var provider = services.BuildServiceProvider();

        var index = provider.GetRequiredService<IWikiIndex>();
        index.Rebuild();

        var session = new VaultSession(
            provider.GetRequiredService<IVaultFileSystem>(),
            index,
            provider.GetRequiredService<ILinkResolver>());
        session.WarmAssets();   // build the image-asset index here (off the UI thread), not on first render
        var daily = provider.GetRequiredService<IDailyNotes>();

        return new OpenResult(session, daily, session.Notes());
    }
}
