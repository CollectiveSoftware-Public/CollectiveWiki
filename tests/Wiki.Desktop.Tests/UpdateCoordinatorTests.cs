// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Desktop.Update;
using Wiki.Update;
using Xunit;

namespace Wiki.Desktop.Tests;

public class UpdateCoordinatorTests
{
    sealed class SpyService(UpdateCheck result) : IUpdateService
    {
        public int Checks { get; private set; }
        public bool Downloaded { get; private set; }
        public IProgress<double>? LastProgress { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public Task<UpdateCheck> CheckAsync(CancellationToken ct) { Checks++; return Task.FromResult(result); }
        public Task<StagedUpdate?> DownloadAsync(UpdateInfo i, IProgress<double>? p, CancellationToken ct)
        {
            Downloaded = true; LastProgress = p; LastToken = ct;
            return Task.FromResult<StagedUpdate?>(null);
        }
        public ApplyOutcome ApplyAndRestart(StagedUpdate s, string exe) => ApplyOutcome.Failed;
    }
    static readonly DateTime Now = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);
    static UpdateCoordinator Make(AppSettings s, SpyService svc) => new(svc, s, persist: () => { });

    [Fact] public void Unset_needs_consent_and_never_auto_checks()
    {
        var svc = new SpyService(new UpdateCheck.UpToDate());
        Assert.Equal(AutoDecision.NeedConsent, Make(new AppSettings { UpdateCheckMode = "Unset" }, svc).DecideAuto(Now));
        Assert.Equal(0, svc.Checks);   // the consent gate held: zero network calls
    }

    [Fact] public void Manual_never_auto_checks()
        => Assert.Equal(AutoDecision.Skip, Make(new AppSettings { UpdateCheckMode = "Manual" }, new SpyService(new UpdateCheck.UpToDate())).DecideAuto(Now));

    [Fact] public void Automatic_checks_when_never_checked_or_over_24h()
    {
        Assert.Equal(AutoDecision.CheckNow, Make(new AppSettings { UpdateCheckMode = "Automatic" }, new SpyService(new UpdateCheck.UpToDate())).DecideAuto(Now));
        Assert.Equal(AutoDecision.CheckNow, Make(new AppSettings { UpdateCheckMode = "Automatic", LastUpdateCheckUtc = Now.AddHours(-25) }, new SpyService(new UpdateCheck.UpToDate())).DecideAuto(Now));
    }

    [Fact] public void Automatic_skips_within_24h()
        => Assert.Equal(AutoDecision.Skip, Make(new AppSettings { UpdateCheckMode = "Automatic", LastUpdateCheckUtc = Now.AddHours(-1) }, new SpyService(new UpdateCheck.UpToDate())).DecideAuto(Now));

    [Fact] public async Task CheckAsync_hits_the_service_and_records_the_timestamp()
    {
        var s = new AppSettings { UpdateCheckMode = "Automatic" };
        var svc = new SpyService(new UpdateCheck.UpToDate());
        int persists = 0;
        var res = await new UpdateCoordinator(svc, s, () => persists++).CheckAsync(Now, default);
        Assert.IsType<UpdateCheck.UpToDate>(res);
        Assert.Equal(1, svc.Checks);
        Assert.Equal(Now, s.LastUpdateCheckUtc);
        Assert.Equal(1, persists);
    }

    [Fact] public async Task DownloadAsync_forwards_progress_and_token_to_the_service()
    {
        // The progress UI passes a reporter (drives the bar) and a token (the Cancel button); both
        // must reach the download path — a swallowed null would leave the dialog frozen at 0%.
        var svc = new SpyService(new UpdateCheck.UpToDate());
        var coord = Make(new AppSettings(), svc);
        var progress = new Progress<double>();
        using var cts = new CancellationTokenSource();
        var info = new UpdateInfo("1.3.0", new ManifestArtifact("win-x64", "https://example.test/app", "abc", 100), "");

        await coord.DownloadAsync(info, progress, cts.Token);

        Assert.True(svc.Downloaded);
        Assert.Same(progress, svc.LastProgress);
        Assert.Equal(cts.Token, svc.LastToken);
    }

    [Fact] public async Task DownloadAsync_defaults_to_no_progress_and_no_token()
    {
        var svc = new SpyService(new UpdateCheck.UpToDate());
        var info = new UpdateInfo("1.3.0", new ManifestArtifact("win-x64", "https://example.test/app", "abc", 100), "");

        await Make(new AppSettings(), svc).DownloadAsync(info);   // silent path stays one-argument

        Assert.True(svc.Downloaded);
        Assert.Null(svc.LastProgress);
        Assert.Equal(CancellationToken.None, svc.LastToken);
    }

    [Fact] public void SkipVersion_and_RecordConsent_persist()
    {
        var s = new AppSettings();
        int persists = 0;
        var c = new UpdateCoordinator(new SpyService(new UpdateCheck.UpToDate()), s, () => persists++);
        c.RecordConsent(automatic: false);
        Assert.Equal("Manual", s.UpdateCheckMode);
        c.SkipVersion("1.3.0");
        Assert.Equal("1.3.0", s.SkippedVersion);
        Assert.Equal(2, persists);
    }
}
