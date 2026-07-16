// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;

namespace Wiki.Update;

public sealed record ManifestArtifact(string Rid, string Url, string Sha256, long Size);

/// <summary>A signed release manifest. <see cref="Verify"/> is a pure port of build/lib/Signing.ps1's
/// Test-ManifestSignature; <see cref="Parse"/> is only ever called after Verify has passed.</summary>
public sealed record UpdateManifest
{
    public required string Version { get; init; }
    public required DateTimeOffset Published { get; init; }
    public required string NotesUrl { get; init; }
    public required IReadOnlyList<ManifestArtifact> Artifacts { get; init; }

    /// <summary>Verifies a detached ECDSA P-256/SHA-256 signature (base64 IEEE-P1363) over the exact
    /// manifest bytes, against a list of trusted base64 SPKI public keys. NEVER throws: any malformed
    /// input — null/empty manifest, empty key list, non-base64 or truncated signature, garbage key —
    /// returns false. A verification failure and a parse failure are the same "do not trust this".
    /// Byte-exact with build/lib/Signing.ps1's Test-ManifestSignature.</summary>
    public static bool Verify(byte[]? manifestBytes, string? signatureBase64, IReadOnlyList<string>? trustedKeysBase64)
    {
        if (manifestBytes is null || manifestBytes.Length == 0) return false;
        if (trustedKeysBase64 is null || trustedKeysBase64.Count == 0) return false;

        byte[] sig;
        try { sig = Convert.FromBase64String(signatureBase64 ?? ""); }
        catch { return false; }

        foreach (var pub in trustedKeysBase64)
        {
            if (string.IsNullOrEmpty(pub)) continue;   // rotation: skip empty entries, don't fail
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(pub), out _);
                if (ecdsa.VerifyData(manifestBytes, sig, HashAlgorithmName.SHA256)) return true;
            }
            catch { /* garbage key: try the next one */ }
        }
        return false;
    }

    /// <summary>The artifact for <paramref name="rid"/> by EXACT ordinal name, or null if the manifest
    /// declares none for it. The app must fetch/execute only this — never an undeclared asset (spec §7).</summary>
    public ManifestArtifact? SelectArtifact(string rid)
        => Artifacts.FirstOrDefault(a => string.Equals(a.Rid, rid, StringComparison.Ordinal));

    private static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web);

    /// <summary>Parse verified bytes into the model. Returns null on any malformed/incomplete manifest.
    /// Call ONLY after Verify has passed — parsing an unverified manifest trusts unauthenticated data.</summary>
    public static UpdateManifest? Parse(byte[] bytes)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<Dto>(bytes, Opts);
            if (dto is null || string.IsNullOrWhiteSpace(dto.Version) || dto.Artifacts is null) return null;
            var arts = new List<ManifestArtifact>();
            foreach (var a in dto.Artifacts)
            {
                if (a is null || string.IsNullOrEmpty(a.Rid) || string.IsNullOrEmpty(a.Url)
                    || string.IsNullOrEmpty(a.Sha256)) return null;
                arts.Add(new ManifestArtifact(a.Rid, a.Url, a.Sha256, a.Size));
            }
            return new UpdateManifest
            {
                Version = dto.Version,
                Published = dto.Published,
                NotesUrl = dto.NotesUrl ?? "",
                Artifacts = arts,
            };
        }
        catch { return null; }
    }

    private sealed class Dto
    {
        public string? Version { get; set; }
        public DateTimeOffset Published { get; set; }
        public string? NotesUrl { get; set; }
        public List<ArtifactDto>? Artifacts { get; set; }
    }
    private sealed class ArtifactDto
    {
        public string? Rid { get; set; }
        public string? Url { get; set; }
        public string? Sha256 { get; set; }
        public long Size { get; set; }
    }
}
