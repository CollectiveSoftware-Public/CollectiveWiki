// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Vault;

namespace Wiki.Core.Tests;

public class WatchDebouncerTests
{
    [Fact]
    public void Coalesces_repeated_modifies_to_one_after_quiet_period()
    {
        var d = new WatchDebouncer();
        d.Observe(new VaultChange(VaultChangeKind.Modified, "A.md", null), tick: 0);
        d.Observe(new VaultChange(VaultChangeKind.Modified, "A.md", null), tick: 10);
        d.Observe(new VaultChange(VaultChangeKind.Modified, "A.md", null), tick: 20);

        Assert.Empty(d.Drain(nowTick: 25, quietTicks: 100));            // still within the quiet window
        var flushed = d.Drain(nowTick: 200, quietTicks: 100);          // quiet long enough
        Assert.Single(flushed);
        Assert.Equal("A.md", flushed[0].Path);
    }

    [Fact]
    public void Distinct_paths_flush_independently()
    {
        var d = new WatchDebouncer();
        d.Observe(new VaultChange(VaultChangeKind.Added, "A.md", null), tick: 0);
        d.Observe(new VaultChange(VaultChangeKind.Modified, "B.md", null), tick: 50);

        var flushed = d.Drain(nowTick: 120, quietTicks: 100);          // A quiet, B not yet
        Assert.Single(flushed);
        Assert.Equal("A.md", flushed[0].Path);

        var rest = d.Drain(nowTick: 200, quietTicks: 100);
        Assert.Single(rest);
        Assert.Equal("B.md", rest[0].Path);
    }

    [Fact]
    public void Latest_event_kind_for_a_path_wins()
    {
        var d = new WatchDebouncer();
        d.Observe(new VaultChange(VaultChangeKind.Added, "A.md", null), tick: 0);
        d.Observe(new VaultChange(VaultChangeKind.Deleted, "A.md", null), tick: 10);

        var flushed = d.Drain(nowTick: 200, quietTicks: 100);
        Assert.Single(flushed);
        Assert.Equal(VaultChangeKind.Deleted, flushed[0].Kind);
    }

    [Fact]
    public void Relative_path_mapping_is_forward_slashed()
    {
        string root = Path.Combine("C:", "vault");
        string full = Path.Combine(root, "sub", "Note.md");
        Assert.Equal("sub/Note.md", FileSystemVaultWatcher.ToRelative(root, full));
    }
}
