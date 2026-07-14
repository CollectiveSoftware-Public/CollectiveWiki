// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Sync;
using Wiki.Core.Vault;
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class P2pSyncEngineTests
{
    private sealed class MapContent : IContentSource
    {
        public readonly Dictionary<string, string> Files = new();
        public string? Read(string path) => Files.TryGetValue(path, out var v) ? v : null;
    }

    [Fact]
    public void Is_a_sync_engine_and_starts_idle()
    {
        ISyncEngine engine = new P2pSyncEngine("A", new MapContent());
        Assert.Equal(SyncStatus.Idle, engine.Status);
    }

    [Fact]
    public void Observing_an_add_records_a_live_entry_in_the_replica()
    {
        var content = new MapContent();
        content.Files["Note.md"] = "hello";
        var engine = new P2pSyncEngine("A", content);

        engine.Observe(new VaultChange(VaultChangeKind.Added, "Note.md", null));

        Assert.Equal("hello", engine.Replica.Read("Note.md"));
        Assert.Equal(1, engine.Replica.Find("Note.md")!.Version["A"]);
    }

    [Fact]
    public void Observing_a_delete_tombstones_the_entry()
    {
        var content = new MapContent();
        content.Files["Note.md"] = "hello";
        var engine = new P2pSyncEngine("A", content);
        engine.Observe(new VaultChange(VaultChangeKind.Added, "Note.md", null));

        content.Files.Remove("Note.md");
        engine.Observe(new VaultChange(VaultChangeKind.Deleted, "Note.md", null));

        Assert.True(engine.Replica.Find("Note.md")!.Deleted);
    }

    [Fact]
    public void Observing_a_rename_tombstones_the_old_path_and_adds_the_new()
    {
        var content = new MapContent();
        content.Files["New.md"] = "body";
        var engine = new P2pSyncEngine("A", content);

        engine.Observe(new VaultChange(VaultChangeKind.Renamed, "New.md", "Old.md"));

        Assert.True(engine.Replica.Find("Old.md")!.Deleted);
        Assert.Equal("body", engine.Replica.Read("New.md"));
    }
}
