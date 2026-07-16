// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Update;

public interface IUpdateApplier
{
    bool IsInstallDirWritable(string currentExePath);
    ApplyOutcome Apply(StagedUpdate staged, string currentExePath);   // does not return on success
}
