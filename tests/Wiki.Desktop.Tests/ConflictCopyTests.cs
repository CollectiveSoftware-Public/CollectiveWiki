// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Desktop.Sync;
using Xunit;

namespace Wiki.Desktop.Tests;

public class ConflictCopyTests
{
    [Theory]
    [InlineData("Note (conflicted copy, ada 2026-07-01).md", true)]
    [InlineData("folder/Note (conflicted copy, bob 2026-07-01).md", true)]
    [InlineData("Note.md", false)]
    [InlineData("Conflicted feelings.md", false)]
    public void Recognizes_conflict_copies(string path, bool expected)
        => Assert.Equal(expected, ConflictCopy.IsConflictNote(path));

    [Fact]
    public void Count_none()
        => Assert.Equal(0, ConflictCopy.Count(new[] { "A.md", "B.md" }));

    [Fact]
    public void Count_one()
        => Assert.Equal(1, ConflictCopy.Count(new[] { "A.md", "A (conflicted copy, ada 2026-07-01).md" }));

    [Fact]
    public void Count_several_including_nested()
        => Assert.Equal(2, ConflictCopy.Count(new[]
        {
            "A (conflicted copy, ada 2026-07-01).md",
            "folder/B (conflicted copy, bob 2026-07-02).md",
            "C.md",
        }));
}
