// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Workspace;
using Xunit;

namespace Wiki.Core.Tests;

public class BookmarkStoreTests
{
    [Fact]
    public void Toggle_adds_then_removes()
    {
        var b = new BookmarkStore(new InMemoryVaultFileSystem());
        b.Toggle("a.md"); Assert.True(b.IsBookmarked("a.md"));
        b.Toggle("a.md"); Assert.False(b.IsBookmarked("a.md"));
    }

    [Fact]
    public void Persists_across_instances()
    {
        var fs = new InMemoryVaultFileSystem();
        new BookmarkStore(fs).Add("n/x.md");
        Assert.Contains("n/x.md", new BookmarkStore(fs).Paths);
    }

    [Fact]
    public void Rename_updates_path_in_place_preserving_order()
    {
        var b = new BookmarkStore(new InMemoryVaultFileSystem());
        b.Add("a.md"); b.Add("b.md"); b.Rename("a.md", "c.md");
        Assert.Equal(new[] { "c.md", "b.md" }, b.Paths);
    }

    [Fact]
    public void Add_is_idempotent_and_Remove_drops_it()
    {
        var b = new BookmarkStore(new InMemoryVaultFileSystem());
        b.Add("a.md"); b.Add("a.md"); Assert.Single(b.Paths);
        b.Remove("a.md"); Assert.Empty(b.Paths);
    }
}
