// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Extensions.DependencyInjection;
using Wiki.Core.DependencyInjection;
using Wiki.Core.Parsing;
using Wiki.Core.Search;
using Wiki.Core.Sync;

namespace Wiki.Core.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddWikiCore_registers_the_stateless_seams()
    {
        var provider = new ServiceCollection().AddWikiCore().BuildServiceProvider();
        Assert.IsType<MarkdigMarkdownParser>(provider.GetRequiredService<IMarkdownParser>());
        Assert.IsType<InMemoryFtsIndex>(provider.GetRequiredService<IFtsIndex>());
        Assert.IsType<LocalNoOpSyncEngine>(provider.GetRequiredService<ISyncEngine>());
    }
}
