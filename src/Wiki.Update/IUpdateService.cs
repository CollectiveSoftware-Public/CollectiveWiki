// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Update;

public interface IUpdateService
{
    Task<UpdateCheck> CheckAsync(CancellationToken ct);
    Task<StagedUpdate?> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct);
    ApplyOutcome ApplyAndRestart(StagedUpdate staged, string currentExePath);
}
