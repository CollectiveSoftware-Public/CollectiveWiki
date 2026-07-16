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
        public Task<UpdateCheck> CheckAsync(CancellationToken ct) { Checks++; return Task.FromResult(result); }
        public Task<StagedUpdate?> DownloadAsync(UpdateInfo i, IProgress<double>? p, CancellationToken ct) => Task.FromResult<StagedUpdate?>(null);
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
