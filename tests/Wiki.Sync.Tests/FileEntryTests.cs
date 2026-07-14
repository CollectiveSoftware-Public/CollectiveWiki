// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class FileEntryTests
{
    [Fact]
    public void Tombstone_is_deleted_with_empty_hash()
    {
        var t = FileEntry.Tombstone("Note.md", VersionVector.Empty.Increment("A"));
        Assert.True(t.Deleted);
        Assert.Equal("", t.ContentHash);
        Assert.Equal("Note.md", t.Path);
    }

    [Fact]
    public void Live_entry_carries_its_hash_and_is_not_deleted()
    {
        var e = new FileEntry("Note.md", VersionVector.Empty.Increment("A"), "abc", false);
        Assert.False(e.Deleted);
        Assert.Equal("abc", e.ContentHash);
    }
}
