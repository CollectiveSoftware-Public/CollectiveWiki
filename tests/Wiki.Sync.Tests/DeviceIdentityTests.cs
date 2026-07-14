// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class DeviceIdentityTests
{
    [Fact]
    public void Two_identities_have_different_device_ids()
    {
        using var a = DeviceIdentity.Create();
        using var b = DeviceIdentity.Create();
        Assert.NotEqual(a.DeviceId, b.DeviceId);
    }

    [Fact]
    public void Device_id_is_52_char_lowercase_base32()
    {
        using var a = DeviceIdentity.Create();
        Assert.Equal(52, a.DeviceId.Length);
        Assert.Matches("^[a-z2-7]{52}$", a.DeviceId);
    }

    [Fact]
    public void Device_id_equals_base32_sha256_of_public_key()
    {
        using var a = DeviceIdentity.Create();
        Assert.Equal(Base32.Encode(SHA256.HashData(a.PublicKey)), a.DeviceId);
    }

    [Fact]
    public void Sign_then_verify_roundtrips()
    {
        using var a = DeviceIdentity.Create();
        var data = Encoding.UTF8.GetBytes("hello");
        var sig = a.Sign(data);
        Assert.True(DeviceIdentity.Verify(a.PublicKey, data, sig));
    }

    [Fact]
    public void Verify_fails_for_tampered_data()
    {
        using var a = DeviceIdentity.Create();
        var sig = a.Sign(Encoding.UTF8.GetBytes("hello"));
        Assert.False(DeviceIdentity.Verify(a.PublicKey, Encoding.UTF8.GetBytes("hell0"), sig));
    }

    [Fact]
    public void Verify_fails_with_a_different_key()
    {
        using var a = DeviceIdentity.Create();
        using var b = DeviceIdentity.Create();
        var data = Encoding.UTF8.GetBytes("hello");
        var sig = a.Sign(data);
        Assert.False(DeviceIdentity.Verify(b.PublicKey, data, sig));
    }

    [Fact]
    public void Export_import_preserves_device_id_and_signing()
    {
        using var a = DeviceIdentity.Create();
        var data = Encoding.UTF8.GetBytes("payload");
        var sig = a.Sign(data);

        using var reloaded = DeviceIdentity.Import(a.Export());

        Assert.Equal(a.DeviceId, reloaded.DeviceId);
        Assert.Equal(a.PublicKey, reloaded.PublicKey);
        // the original signature still verifies against the reloaded public key...
        Assert.True(DeviceIdentity.Verify(reloaded.PublicKey, data, sig));
        // ...and the reloaded identity can sign anew, verified against the original public key.
        Assert.True(DeviceIdentity.Verify(a.PublicKey, data, reloaded.Sign(data)));
    }
}
