// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;

namespace Wiki.Update.Tests;

public class StagerTests
{
    sealed class FakeDownloader(byte[] payload) : IUpdateDownloader
    {
        public List<Uri> Fetched { get; } = new();
        public Task<byte[]> GetBytesAsync(Uri url, IProgress<double>? p, CancellationToken ct)
        { Fetched.Add(url); return Task.FromResult(payload); }
    }
    static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
    static string TempDir() { var d = Path.Combine(Path.GetTempPath(), "wu-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

    [Fact] public async Task Stages_when_hash_matches_and_fetches_only_the_declared_url()
    {
        var payload = new byte[] { 1, 2, 3, 4 };
        var dl = new FakeDownloader(payload);
        var art = new ManifestArtifact("win-x64", "https://example/w.exe", Sha(payload), payload.Length);
        var info = new UpdateInfo("1.1.0", art, "n");
        var dir = TempDir();
        var staged = await new UpdateStager(dl).StageAsync(info, dir, null, default);
        Assert.NotNull(staged);
        Assert.Equal(payload, await File.ReadAllBytesAsync(staged!.FilePath));
        Assert.Single(dl.Fetched);                                       // only ONE fetch
        Assert.Equal("https://example/w.exe", dl.Fetched[0].ToString()); // the declared artifact only
        Assert.Empty(Directory.GetFiles(dir, "*.part"));                 // .part promoted/cleaned
    }

    [Fact] public async Task Discards_and_returns_null_on_hash_mismatch()
    {
        var payload = new byte[] { 1, 2, 3, 4 };
        var art = new ManifestArtifact("win-x64", "https://example/w.exe", Sha(new byte[] { 9 }), 4); // wrong hash
        var dir = TempDir();
        var staged = await new UpdateStager(new FakeDownloader(payload)).StageAsync(new("1.1.0", art, "n"), dir, null, default);
        Assert.Null(staged);
        Assert.Empty(Directory.GetFiles(dir));                           // nothing left behind
    }
}
