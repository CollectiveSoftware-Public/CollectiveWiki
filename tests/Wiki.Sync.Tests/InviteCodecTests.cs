// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class InviteCodecTests
{
    private static readonly Guid Vault = new("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Expiry = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static InvitePayload Sample() => new(
        "ownerdeviceid", Vault, PeerRole.ReadWrite,
        new byte[] { 1, 2, 3, 4, 5 }, Expiry, "lan:192.168.1.5:8443");

    [Fact]
    public void Encoded_invite_uses_the_cwiki_scheme()
        => Assert.StartsWith("cwiki://invite/", InviteCodec.Encode(Sample()));

    [Fact]
    public void Round_trips_all_fields()
    {
        Assert.True(InviteCodec.TryParse(InviteCodec.Encode(Sample()), out var parsed));
        Assert.Equal("ownerdeviceid", parsed!.OwnerDeviceId);
        Assert.Equal(Vault, parsed.VaultId);
        Assert.Equal(PeerRole.ReadWrite, parsed.Role);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, parsed.PairingToken);
        Assert.Equal(Expiry, parsed.ExpiresAt);
        Assert.Equal("lan:192.168.1.5:8443", parsed.DiscoveryHint);
    }

    [Fact]
    public void A_read_only_invite_round_trips_its_role()
    {
        var ro = Sample() with { Role = PeerRole.ReadOnly };
        Assert.True(InviteCodec.TryParse(InviteCodec.Encode(ro), out var parsed));
        Assert.Equal(PeerRole.ReadOnly, parsed!.Role);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-cwiki-invite")]
    [InlineData("cwiki://invite/!!!not-base64!!!")]
    [InlineData("cwiki://invite/QUJD")]   // decodes to "ABC" — valid base64, not a valid payload
    public void Garbage_fails_to_parse(string text)
        => Assert.False(InviteCodec.TryParse(text, out _));

    [Fact]
    public void A_truncated_blob_fails_to_parse()
    {
        var encoded = InviteCodec.Encode(Sample());
        var truncated = encoded[..(encoded.Length - 5)];
        Assert.False(InviteCodec.TryParse(truncated, out _));
    }
}
