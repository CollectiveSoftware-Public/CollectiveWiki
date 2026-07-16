// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;

namespace Wiki.Update;

/// <summary>Downloads exactly the declared artifact to a .part file — capped at its declared size so a
/// hostile mirror cannot force an unbounded download — verifies its SHA-256 against the manifest, and
/// promotes it to a staged file only on match. Returns null (and leaves nothing behind) on a hash
/// mismatch or a version/rid that would escape the stage directory: a corrupt download and an attack
/// are indistinguishable, so neither is applied.</summary>
public sealed class UpdateStager(IUpdateDownloader downloader)
{
    /// <summary>Headroom over the manifest's declared artifact size (real bytes can differ slightly).</summary>
    public const long SizeSlackBytes = 1024 * 1024;

    /// <summary>Cap when the manifest declares no size — well above a real ~100MB build, well below OOM.</summary>
    public const long DefaultArtifactMaxBytes = 512L * 1024 * 1024;

    public async Task<StagedUpdate?> StageAsync(UpdateInfo info, string stageDir, IProgress<double>? progress, CancellationToken ct)
    {
        // Version/rid come from a signed manifest in the intended flow, but the stager must not trust
        // that on faith: a component carrying a separator, "..", or a root could steer the staged path
        // out of the stage directory. Reject those outright, then verify containment as a backstop.
        if (!IsSafeComponent(info.Version) || !IsSafeComponent(info.Artifact.Rid)) return null;
        Directory.CreateDirectory(stageDir);
        var stageRoot = Path.GetFullPath(stageDir);
        var part = Path.GetFullPath(Path.Combine(stageDir, $"{info.Version}-{info.Artifact.Rid}.part"));
        var final = Path.GetFullPath(Path.Combine(stageDir, $"{info.Version}-{info.Artifact.Rid}"));
        if (!IsUnder(stageRoot, part) || !IsUnder(stageRoot, final)) return null;

        var cap = info.Artifact.Size > 0 ? info.Artifact.Size + SizeSlackBytes : DefaultArtifactMaxBytes;
        try
        {
            var bytes = await downloader.GetBytesAsync(new Uri(info.Artifact.Url), cap, progress, ct);
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

    private static bool IsUnder(string root, string path)
        => path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    /// <summary>A path component that is a single plain name — no separator, no "..", not rooted — so it
    /// cannot navigate out of the directory it is combined into.</summary>
    private static bool IsSafeComponent(string s)
        => !string.IsNullOrEmpty(s)
           && s.IndexOf('/') < 0 && s.IndexOf('\\') < 0
           && !s.Contains("..", StringComparison.Ordinal)
           && !Path.IsPathRooted(s);
}
