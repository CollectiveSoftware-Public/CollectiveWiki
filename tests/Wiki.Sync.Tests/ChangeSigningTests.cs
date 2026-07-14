// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Sync;

namespace Wiki.Sync.Tests;

public class ChangeSigningTests
{
    private static FileEntry Entry(string path, VersionVector v, string hash = "abc", bool deleted = false)
        => new(path, v, hash, deleted);

    [Fact]
    public void Canonical_bytes_are_stable_for_semantically_equal_vectors()
    {
        var v1 = VersionVector.Empty.Increment("A");                        // {A:1}
        var v2 = VersionVector.Empty.Increment("A").Merge(VersionVector.Empty); // still {A:1}
        Assert.Equal(
            ChangeCanonical.Bytes(Entry("Note.md", v1), "A"),
            ChangeCanonical.Bytes(Entry("Note.md", v2), "A"));
    }

    [Fact]
    public void Canonical_bytes_differ_when_the_content_hash_differs()
    {
        var v = VersionVector.Empty.Increment("A");
        Assert.NotEqual(
            ChangeCanonical.Bytes(Entry("Note.md", v, "aaa"), "A"),
            ChangeCanonical.Bytes(Entry("Note.md", v, "bbb"), "A"));
    }

    [Fact]
    public void Signer_signs_as_its_own_device_id()
    {
        using var id = DeviceIdentity.Create();
        var signer = new ChangeSigner(id);
        var signed = signer.Sign(Entry("Note.md", VersionVector.Empty.Increment(id.DeviceId)));
        Assert.Equal(id.DeviceId, signed.Signer);
    }

    [Fact]
    public void Signed_entry_verifies_via_the_device_public_key()
    {
        using var id = DeviceIdentity.Create();
        var entry = Entry("Note.md", VersionVector.Empty.Increment(id.DeviceId));
        var signed = new ChangeSigner(id).Sign(entry);
        Assert.True(DeviceIdentity.Verify(
            id.PublicKey, ChangeCanonical.Bytes(entry, id.DeviceId), signed.Signature));
    }

    [Fact]
    public void SignIndex_signs_every_entry()
    {
        using var id = DeviceIdentity.Create();
        var v = VersionVector.Empty.Increment(id.DeviceId);
        var signed = new ChangeSigner(id).SignIndex(new[] { Entry("A.md", v), Entry("B.md", v) });
        Assert.Equal(2, signed.Count);
        Assert.All(signed, s => Assert.Equal(id.DeviceId, s.Signer));
    }
}
