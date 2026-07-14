// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class ContentKeySealerTests
{
    private static AuthorizedPeer Peer(DeviceIdentity id)
        => new(id.DeviceId, id.PublicKey, PeerRole.ReadWrite, "n", "e");

    private readonly ContentKeySealer _sealer = new();

    [Fact]
    public void Ecdh_shared_secret_is_symmetric()
    {
        using var a = DeviceIdentity.Create();
        using var b = DeviceIdentity.Create();
        Assert.Equal(a.DeriveSharedSecret(b.PublicKey), b.DeriveSharedSecret(a.PublicKey));
    }

    [Fact]
    public void Recipient_unseals_the_content_key()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var key = ContentKey.Generate(0);

        var sealed_ = _sealer.Seal(owner, Peer(alice), key);
        var recovered = _sealer.Unseal(alice, owner.PublicKey, owner.DeviceId, sealed_);

        Assert.NotNull(recovered);
        Assert.Equal(key.Key, recovered!.Key);
        Assert.Equal(0, recovered.Epoch);
        Assert.Equal(alice.DeviceId, sealed_.RecipientDeviceId);
    }

    [Fact]
    public void A_different_device_cannot_unseal()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        using var eve = DeviceIdentity.Create();
        var sealed_ = _sealer.Seal(owner, Peer(alice), ContentKey.Generate(3));
        Assert.Null(_sealer.Unseal(eve, owner.PublicKey, owner.DeviceId, sealed_));
    }

    [Fact]
    public void A_tampered_ciphertext_fails_authentication()
    {
        using var owner = DeviceIdentity.Create();
        using var alice = DeviceIdentity.Create();
        var sealed_ = _sealer.Seal(owner, Peer(alice), ContentKey.Generate(0));
        var corrupt = sealed_ with { Ciphertext = (byte[])sealed_.Ciphertext.Clone() };
        corrupt.Ciphertext[0] ^= 0xff;
        Assert.Null(_sealer.Unseal(alice, owner.PublicKey, owner.DeviceId, corrupt));
    }
}
