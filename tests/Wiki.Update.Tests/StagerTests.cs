// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;

namespace Wiki.Update.Tests;

public class StagerTests
{
    sealed class FakeDownloader(byte[] payload) : IUpdateDownloader
    {
        public List<Uri> Fetched { get; } = new();
        public long? LastCap { get; private set; }
        public Task<byte[]> GetBytesAsync(Uri url, long maxBytes, IProgress<double>? p, CancellationToken ct)
        {
            Fetched.Add(url);
            LastCap = maxBytes;
            return Task.FromResult(payload);
        }
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

    [Fact] public async Task Bounds_the_download_by_the_declared_size_not_unbounded()
    {
        var payload = new byte[] { 1, 2, 3, 4 };
        var dl = new FakeDownloader(payload);
        var art = new ManifestArtifact("win-x64", "https://example/w.exe", Sha(payload), payload.Length);
        await new UpdateStager(dl).StageAsync(new("1.1.0", art, "n"), TempDir(), null, default);
        Assert.NotNull(dl.LastCap);
        Assert.Equal(payload.Length + UpdateStager.SizeSlackBytes, dl.LastCap);   // declared size + slack, not unbounded
    }

    [Fact] public async Task Falls_back_to_a_bounded_cap_when_the_size_is_absent()
    {
        var payload = new byte[] { 1, 2, 3, 4 };
        var dl = new FakeDownloader(payload);
        var art = new ManifestArtifact("win-x64", "https://example/w.exe", Sha(payload), 0);   // no declared size
        await new UpdateStager(dl).StageAsync(new("1.1.0", art, "n"), TempDir(), null, default);
        Assert.Equal(UpdateStager.DefaultArtifactMaxBytes, dl.LastCap);
    }

    [Theory]
    [InlineData("../../evil", "win-x64")]     // traversal in the version
    [InlineData("1.1.0", "../../evil")]       // traversal in the rid
    public async Task Rejects_a_path_escaping_component_without_downloading(string version, string rid)
    {
        var dl = new FakeDownloader(new byte[] { 1 });
        var art = new ManifestArtifact(rid, "https://example/w.exe", "aa", 1);
        var dir = TempDir();
        var staged = await new UpdateStager(dl).StageAsync(new UpdateInfo(version, art, "n"), dir, null, default);
        Assert.Null(staged);                  // refused
        Assert.Empty(dl.Fetched);             // and refused BEFORE any network fetch
    }
}
