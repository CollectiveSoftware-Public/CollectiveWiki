// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace Wiki.Sync;

/// <summary>Deterministic canonical bytes for a change, signed by its author. Both peers must produce
/// identical bytes for the same logical change, so the version vector is emitted with devices sorted by
/// ordinal id and zero counters filtered out. Content is bound by the entry's content hash, not embedded
/// — large notes are never signed directly.</summary>
public static class ChangeCanonical
{
    public static byte[] Bytes(FileEntry entry, string signer)
    {
        var vector = string.Join(",",
            entry.Version.Devices
                .Where(d => entry.Version[d] > 0)
                .OrderBy(d => d, StringComparer.Ordinal)
                .Select(d => $"{d}={entry.Version[d]}"));

        var canonical =
            $"cwikichange/v1\n{entry.Path}\n{vector}\n{entry.ContentHash}\n{(entry.Deleted ? 1 : 0)}\n{signer}";
        return Encoding.UTF8.GetBytes(canonical);
    }
}

/// <summary>A <see cref="FileEntry"/> plus the id of the device that vouches for it and that device's
/// signature over the canonical change bytes. This is the authenticated unit two peers exchange.</summary>
public sealed record SignedFileEntry(FileEntry Entry, string Signer, byte[] Signature);

/// <summary>Signs a device's index entries as itself, so peers can verify authenticity and check the
/// author's role before applying.</summary>
public sealed class ChangeSigner(DeviceIdentity identity)
{
    private readonly DeviceIdentity _identity = identity;

    public SignedFileEntry Sign(FileEntry entry)
        => new(entry, _identity.DeviceId, _identity.Sign(ChangeCanonical.Bytes(entry, _identity.DeviceId)));

    public IReadOnlyList<SignedFileEntry> SignIndex(IEnumerable<FileEntry> index)
        => index.Select(Sign).ToList();
}
