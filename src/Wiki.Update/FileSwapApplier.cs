// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;

namespace Wiki.Update;

/// <summary>Applies an update by an atomic single-file swap (spec §5.4). The launch and process-exit are
/// injected so the swap is unit-testable without actually restarting. Failure mode is always "nothing
/// happened", never "no app": a failed move undoes the .old rename.</summary>
public sealed class FileSwapApplier(Action<string> launch, Action<int> exit) : IUpdateApplier
{
    public bool IsInstallDirWritable(string currentExePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(currentExePath))!;
            var probe = Path.Combine(dir, $".wcw-write-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    public ApplyOutcome Apply(StagedUpdate staged, string currentExePath)
    {
        var cur = Path.GetFullPath(currentExePath);
        if (!IsInstallDirWritable(cur)) return ApplyOutcome.NotWritable;
        var old = cur + ".old";
        try { if (File.Exists(old)) File.Delete(old); } catch { /* stale .old; best effort */ }

        File.Move(cur, old);                       // running exe can be renamed
        try
        {
            File.Move(staged.FilePath, cur);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                File.SetUnixFileMode(cur, UnixFileModeExecutable());
        }
        catch
        {
            try { File.Move(old, cur); } catch { /* leave .old for recovery */ }  // undo
            return ApplyOutcome.Failed;
        }
        launch(cur);
        exit(0);
        return ApplyOutcome.Failed;                // unreachable when exit() really exits
    }

    private static UnixFileMode UnixFileModeExecutable()
        => UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
         | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
         | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
}
