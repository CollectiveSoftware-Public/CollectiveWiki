// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;

namespace Wiki.Update;

/// <summary>Downloads exactly the declared artifact to a .part file, verifies its SHA-256 against the
/// manifest, and promotes it to a staged file only on match. Returns null (and leaves nothing behind)
/// on mismatch — a corrupt download and an attack are indistinguishable, so neither is applied.</summary>
public sealed class UpdateStager(IUpdateDownloader downloader)
{
    public async Task<StagedUpdate?> StageAsync(UpdateInfo info, string stageDir, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(stageDir);
        var part = Path.Combine(stageDir, $"{info.Version}-{info.Artifact.Rid}.part");
        var final = Path.Combine(stageDir, $"{info.Version}-{info.Artifact.Rid}");
        try
        {
            var bytes = await downloader.GetBytesAsync(new Uri(info.Artifact.Url), progress, ct);
            var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(actual, info.Artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                return null;                                    // fail closed: do not stage
            await File.WriteAllBytesAsync(part, bytes, ct);
            if (File.Exists(final)) File.Delete(final);
            File.Move(part, final);
            return new StagedUpdate(final, info.Version);
        }
        finally
        {
            if (File.Exists(part)) { try { File.Delete(part); } catch { /* best effort */ } }
        }
    }
}
