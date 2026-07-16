// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Update.Tests;

public class VerifyTests
{
    static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static byte[] Manifest() => File.ReadAllBytes(Path.Combine(Dir, "manifest.json"));
    static string Sig()      => File.ReadAllText(Path.Combine(Dir, "manifest.json.sig"));
    static string Key()      => File.ReadAllText(Path.Combine(Dir, "pubkey.txt"));

    [Fact] public void Accepts_a_genuine_PowerShell_signed_manifest()
        => Assert.True(UpdateManifest.Verify(Manifest(), Sig(), new[] { Key() }));

    [Fact] public void Rejects_a_tampered_manifest()
    {
        var bytes = Manifest();
        bytes[10] ^= 0xFF;                                   // flip a byte
        Assert.False(UpdateManifest.Verify(bytes, Sig(), new[] { Key() }));
    }

    [Fact] public void Rejects_a_wrong_key()
    {
        using var other = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var wrong = Convert.ToBase64String(other.ExportSubjectPublicKeyInfo());
        Assert.False(UpdateManifest.Verify(Manifest(), Sig(), new[] { wrong }));
    }

    [Fact] public void Rejects_a_truncated_signature_without_throwing()
    {
        var truncated = Sig().Substring(0, Sig().Length / 2);
        Assert.False(UpdateManifest.Verify(Manifest(), truncated, new[] { Key() }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("!!!not base64!!!")]
    public void Rejects_garbage_signature_without_throwing(string? sig)
        => Assert.False(UpdateManifest.Verify(Manifest(), sig, new[] { Key() }));

    [Fact] public void Rejects_null_or_empty_manifest()
    {
        Assert.False(UpdateManifest.Verify(null, Sig(), new[] { Key() }));
        Assert.False(UpdateManifest.Verify(Array.Empty<byte>(), Sig(), new[] { Key() }));
    }

    [Fact] public void Rejects_empty_key_list()
        => Assert.False(UpdateManifest.Verify(Manifest(), Sig(), Array.Empty<string>()));

    [Fact] public void Rotation_a_null_or_garbage_key_beside_the_real_one_still_verifies()
        => Assert.True(UpdateManifest.Verify(Manifest(), Sig(), new[] { "", "not-a-key", Key() }));
}
