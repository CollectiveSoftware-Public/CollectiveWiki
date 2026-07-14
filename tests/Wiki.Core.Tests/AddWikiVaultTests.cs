// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Extensions.DependencyInjection;
using Wiki.Core.DependencyInjection;
using Wiki.Core.Embedding;
using Wiki.Core.Graph;
using Wiki.Core.Indexing;
using Wiki.Core.Journal;
using Wiki.Core.Templating;
using Wiki.Core.Time;
using Wiki.Core.Vault;

namespace Wiki.Core.Tests;

public class AddWikiVaultTests
{
    [Fact]
    public void Core_registrations_include_clock_and_templates()
    {
        var sp = new ServiceCollection().AddWikiCore().BuildServiceProvider();
        Assert.IsType<SystemClock>(sp.GetRequiredService<IClock>());
        Assert.IsType<TemplateEngine>(sp.GetRequiredService<ITemplateEngine>());
    }

    [Fact]
    public void Vault_registrations_resolve_the_full_graph_of_services()
    {
        string root = Path.Combine(Path.GetTempPath(), "wiki-di-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Home.md"), "# Home\n[[Home]]");
        try
        {
            var sp = new ServiceCollection()
                .AddWikiCore()
                .AddWikiVault(root)
                .BuildServiceProvider();

            Assert.IsType<PhysicalVaultFileSystem>(sp.GetRequiredService<IVaultFileSystem>());
            Assert.IsType<LinkResolver>(sp.GetRequiredService<ILinkResolver>());
            Assert.IsType<WikiIndex>(sp.GetRequiredService<IWikiIndex>());
            Assert.IsType<FileSystemVaultWatcher>(sp.GetRequiredService<IVaultWatcher>());
            Assert.IsType<TransclusionResolver>(sp.GetRequiredService<ITransclusionResolver>());
            Assert.IsType<DailyNotes>(sp.GetRequiredService<IDailyNotes>());

            sp.GetRequiredService<IWikiIndex>().Rebuild();
            var graph = sp.GetRequiredService<IGraphModel>();
            Assert.Contains(graph.Nodes, n => n.NotePath == "Home.md");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
