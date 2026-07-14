// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class ConflictCopyNameTests
{
    private static readonly DateTimeOffset When = new(2026, 6, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Root_note_gets_the_conflicted_copy_suffix()
        => Assert.Equal("Note (conflicted copy, deviceAA 2026-06-30).md",
                        ConflictCopyName.For("Note.md", "deviceAAlong", When));

    [Fact]
    public void Nested_note_keeps_its_folder()
        => Assert.Equal("sub/Note (conflicted copy, B 2026-06-30).md",
                        ConflictCopyName.For("sub/Note.md", "B", When));

    [Fact]
    public void Backslashes_are_normalised_to_forward_slashes()
        => Assert.Equal("a/b/Note (conflicted copy, B 2026-06-30).md",
                        ConflictCopyName.For(@"a\b\Note.md", "B", When));

    [Fact]
    public void Same_inputs_produce_the_same_name_on_both_peers()
        => Assert.Equal(ConflictCopyName.For("Note.md", "B", When),
                        ConflictCopyName.For("Note.md", "B", When));
}
