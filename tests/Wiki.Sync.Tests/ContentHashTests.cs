// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class ContentHashTests
{
    [Fact]
    public void Same_text_hashes_the_same() => Assert.Equal(ContentHash.Of("hello"), ContentHash.Of("hello"));

    [Fact]
    public void Different_text_hashes_differently() => Assert.NotEqual(ContentHash.Of("a"), ContentHash.Of("b"));

    [Fact]
    public void Hash_is_64_hex_chars_lowercase()
    {
        var h = ContentHash.Of("hello");
        Assert.Equal(64, h.Length);
        Assert.Matches("^[0-9a-f]{64}$", h);
    }

    [Fact]
    public void Known_vector_for_empty_string()
        => Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", ContentHash.Of(""));
}
