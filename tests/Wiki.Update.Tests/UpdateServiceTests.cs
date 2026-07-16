// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Wiki.Update.Tests;

public class UpdateServiceTests
{
    // Fake downloader mapping url -> bytes; records fetches.
    sealed class MapDownloader(Dictionary<string, byte[]> map) : IUpdateDownloader
    {
        public List<Uri> Fetched { get; } = new();
        public Task<byte[]> GetBytesAsync(Uri url, IProgress<double>? p, CancellationToken ct)
        {
            Fetched.Add(url);
            return map.TryGetValue(url.ToString(), out var b)
                ? Task.FromResult(b)
                : throw new HttpRequestException("404");
        }
    }

    static (byte[] manifest, string sigB64, string keyB64) SignedManifest(string version, byte[] artifact)
    {
        using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sha = Convert.ToHexString(SHA256.HashData(artifact)).ToLowerInvariant();
        var manifest = new
        {
            version,
            published = "2026-07-20T14:00:00Z",
            notesUrl = "n",
            artifacts = new[] { new { rid = "win-x64", url = "https://ex/app", sha256 = sha, size = artifact.Length } }
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var sig = Convert.ToBase64String(k.SignData(bytes, HashAlgorithmName.SHA256));
        return (bytes, sig, Convert.ToBase64String(k.ExportSubjectPublicKeyInfo()));
    }

    static UpdateFeed Feed(string current, string key, string? skip = null) => new(
        new Uri("https://ex/manifest.json"), new Uri("https://ex/manifest.json.sig"),
        new[] { key }, current, "win-x64", skip);

    static UpdateService Svc(UpdateFeed feed, MapDownloader dl)
        => new(feed, dl, new FileSwapApplier(_ => { }, _ => { }), "stage");

    [Fact] public async Task Available_when_a_newer_signed_manifest_exists()
    {
        var art = Encoding.UTF8.GetBytes("APP");
        var (m, sig, key) = SignedManifest("1.1.0", art);
        var dl = new MapDownloader(new()
        {
            ["https://ex/manifest.json"] = m,
            ["https://ex/manifest.json.sig"] = Encoding.UTF8.GetBytes(sig),
        });
        var res = await Svc(Feed("1.0.0", key), dl).CheckAsync(default);
        var a = Assert.IsType<UpdateCheck.Available>(res);
        Assert.Equal("1.1.0", a.Info.Version);
        Assert.Equal("https://ex/app", a.Info.Artifact.Url);
    }

    [Fact] public async Task UpToDate_when_not_newer()
    {
        var (m, sig, key) = SignedManifest("1.0.0", Encoding.UTF8.GetBytes("APP"));
        var dl = new MapDownloader(new()
        {
            ["https://ex/manifest.json"] = m,
            ["https://ex/manifest.json.sig"] = Encoding.UTF8.GetBytes(sig),
        });
        var res = await Svc(Feed("1.0.0", key), dl).CheckAsync(default);
        Assert.IsType<UpdateCheck.UpToDate>(res);
    }

    [Fact] public async Task Failed_and_no_artifact_fetch_when_signature_is_wrong()
    {
        var (m, _, key) = SignedManifest("1.1.0", Encoding.UTF8.GetBytes("APP"));
        var badSig = Convert.ToBase64String(new byte[64]);       // zeros: valid base64, invalid sig
        var dl = new MapDownloader(new()
        {
            ["https://ex/manifest.json"] = m,
            ["https://ex/manifest.json.sig"] = Encoding.UTF8.GetBytes(badSig),
        });
        var res = await Svc(Feed("1.0.0", key), dl).CheckAsync(default);
        Assert.IsType<UpdateCheck.Failed>(res);
        Assert.DoesNotContain(dl.Fetched, u => u.ToString() == "https://ex/app");  // never touched the binary
    }

    [Fact] public async Task Failed_on_network_error()
    {
        var dl = new MapDownloader(new());                      // manifest url missing -> throws
        var res = await Svc(Feed("1.0.0", "x"), dl).CheckAsync(default);
        Assert.IsType<UpdateCheck.Failed>(res);
    }
}
