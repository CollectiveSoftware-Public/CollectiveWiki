// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace Wiki.Update;

/// <summary>Orchestrates check → download → apply against an UpdateFeed. Verify-then-parse-then-select
/// ordering is load-bearing: the signature is checked over the exact fetched bytes BEFORE the manifest
/// is parsed or any artifact URL is touched. Every failure path yields Failed/UpToDate — never a throw
/// to the caller, never a retry loop, never "apply anyway" (spec §6).</summary>
public sealed class UpdateService(
    UpdateFeed feed, IUpdateDownloader downloader, IUpdateApplier applier, string stageDir) : IUpdateService
{
    public async Task<UpdateCheck> CheckAsync(CancellationToken ct)
    {
        byte[] manifestBytes, sigBytes;
        try
        {
            manifestBytes = await downloader.GetBytesAsync(feed.ManifestUrl, null, ct);
            sigBytes = await downloader.GetBytesAsync(feed.SignatureUrl, null, ct);
        }
        catch (Exception e) { return new UpdateCheck.Failed($"fetch failed: {e.Message}"); }

        var sigB64 = Encoding.UTF8.GetString(sigBytes).Trim();
        if (!UpdateManifest.Verify(manifestBytes, sigB64, feed.TrustedKeys))   // hostile/corrupt: fail closed
            return new UpdateCheck.Failed("signature did not verify");

        var manifest = UpdateManifest.Parse(manifestBytes);
        if (manifest is null) return new UpdateCheck.Failed("malformed manifest");

        var artifact = manifest.SelectArtifact(feed.Rid);
        if (artifact is null) return new UpdateCheck.UpToDate();               // no build for this RID

        if (!UpdatePolicy.ShouldOffer(manifest.Version, feed.CurrentVersion, feed.SkippedVersion))
            return new UpdateCheck.UpToDate();

        return new UpdateCheck.Available(new UpdateInfo(manifest.Version, artifact, manifest.NotesUrl));
    }

    public Task<StagedUpdate?> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct)
        => new UpdateStager(downloader).StageAsync(info, stageDir, progress, ct);

    public ApplyOutcome ApplyAndRestart(StagedUpdate staged, string currentExePath)
        => applier.Apply(staged, currentExePath);
}
