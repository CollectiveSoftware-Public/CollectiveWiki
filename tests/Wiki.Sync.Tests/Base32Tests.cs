// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class Base32Tests
{
    // RFC 4648 §10 test vectors, lowercased with padding stripped.
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "my")]
    [InlineData("fo", "mzxq")]
    [InlineData("foo", "mzxw6")]
    [InlineData("foob", "mzxw6yq")]
    [InlineData("fooba", "mzxw6ytb")]
    [InlineData("foobar", "mzxw6ytboi")]
    public void Encodes_rfc4648_vectors(string input, string expected)
        => Assert.Equal(expected, Base32.Encode(Encoding.UTF8.GetBytes(input)));

    [Fact]
    public void Uses_only_the_lowercase_base32_alphabet()
    {
        var encoded = Base32.Encode(new byte[] { 0xff, 0x00, 0xab, 0xcd, 0xef });
        Assert.Matches("^[a-z2-7]+$", encoded);
    }

    [Fact]
    public void A_32_byte_hash_encodes_to_52_chars()
        => Assert.Equal(52, Base32.Encode(new byte[32]).Length);
}
