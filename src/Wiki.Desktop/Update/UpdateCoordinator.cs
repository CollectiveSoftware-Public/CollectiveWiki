// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Update;

namespace Wiki.Desktop.Update;

public enum AutoDecision { NeedConsent, Skip, CheckNow }

/// <summary>UI-free brain for update checks. The head asks <see cref="DecideAuto"/> at startup and only
/// calls <see cref="CheckAsync"/> — the sole method that touches the network — when the answer is
/// CheckNow, so an Unset (no-consent) or throttled state performs ZERO network traffic.</summary>
public sealed class UpdateCoordinator(IUpdateService service, AppSettings settings, Action persist)
{
    public AutoDecision DecideAuto(DateTime nowUtc)
    {
        if (settings.UpdateCheckMode == "Unset") return AutoDecision.NeedConsent;
        if (settings.UpdateCheckMode != "Automatic") return AutoDecision.Skip;          // Manual
        return UpdatePolicy.IsCheckDue(settings.LastUpdateCheckUtc, nowUtc) ? AutoDecision.CheckNow : AutoDecision.Skip;
    }

    public async Task<UpdateCheck> CheckAsync(DateTime nowUtc, CancellationToken ct)
    {
        var result = await service.CheckAsync(ct);
        settings.LastUpdateCheckUtc = nowUtc;
        persist();
        return result;
    }

    public void RecordConsent(bool automatic)
    {
        settings.UpdateCheckMode = automatic ? "Automatic" : "Manual";
        persist();
    }

    public void SkipVersion(string version)
    {
        settings.SkippedVersion = version;
        persist();
    }

    // Thin pass-throughs so the head never reaches past the coordinator.
    public Task<StagedUpdate?> DownloadAsync(UpdateInfo info) => service.DownloadAsync(info, null, default);
    public ApplyOutcome Apply(StagedUpdate staged, string exePath) => service.ApplyAndRestart(staged, exePath);
}
