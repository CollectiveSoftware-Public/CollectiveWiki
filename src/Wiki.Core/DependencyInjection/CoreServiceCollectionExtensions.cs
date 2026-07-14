// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wiki.Core.Editor;
using Wiki.Core.Embedding;
using Wiki.Core.Graph;
using Wiki.Core.Indexing;
using Wiki.Core.Journal;
using Wiki.Core.Parsing;
using Wiki.Core.Search;
using Wiki.Core.Sync;
using Wiki.Core.Templating;
using Wiki.Core.Time;
using Wiki.Core.Vault;

namespace Wiki.Core.DependencyInjection;

/// <summary>Registers Wiki.Core services. <see cref="AddWikiCore"/> wires the stateless seams; the
/// vault-scoped seams (which need a vault root at runtime) are wired by <see cref="AddWikiVault"/>.</summary>
public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddWikiCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IMarkdownParser, MarkdigMarkdownParser>();
        services.TryAddSingleton<IFtsIndex, InMemoryFtsIndex>();
        services.TryAddSingleton<ISyncEngine, LocalNoOpSyncEngine>();
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<ITemplateEngine, TemplateEngine>();
        services.TryAddSingleton<EditorModel>();
        return services;
    }

    /// <summary>Registers the vault-scoped seams for the vault rooted at <paramref name="vaultRoot"/>.
    /// Call after <see cref="AddWikiCore"/>. The link resolver is alias-aware (it gets the parser).</summary>
    public static IServiceCollection AddWikiVault(
        this IServiceCollection services, string vaultRoot, DailyNoteOptions? dailyNotes = null)
    {
        services.TryAddSingleton<IVaultFileSystem>(_ => new PhysicalVaultFileSystem(vaultRoot));
        services.TryAddSingleton<ILinkResolver>(sp =>
            new LinkResolver(sp.GetRequiredService<IVaultFileSystem>(), sp.GetRequiredService<IMarkdownParser>()));
        services.TryAddSingleton<IWikiIndex>(sp => new WikiIndex(
            sp.GetRequiredService<IVaultFileSystem>(),
            sp.GetRequiredService<IMarkdownParser>(),
            sp.GetRequiredService<ILinkResolver>(),
            sp.GetRequiredService<IFtsIndex>()));
        services.TryAddSingleton<IVaultWatcher>(_ => new FileSystemVaultWatcher(vaultRoot));
        services.TryAddSingleton<HeadingSectionExtractor>();
        services.TryAddSingleton<ITransclusionResolver>(sp => new TransclusionResolver(
            sp.GetRequiredService<IVaultFileSystem>(),
            sp.GetRequiredService<ILinkResolver>(),
            sp.GetRequiredService<HeadingSectionExtractor>()));
        services.TryAddSingleton<IDailyNotes>(sp => new DailyNotes(
            sp.GetRequiredService<IVaultFileSystem>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ITemplateEngine>(),
            dailyNotes ?? DailyNoteOptions.Default));
        services.TryAddTransient<IGraphModel>(sp =>
            GraphModel.Build(sp.GetRequiredService<IWikiIndex>(), sp.GetRequiredService<ILinkResolver>()));
        return services;
    }
}
