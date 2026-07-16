// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Update.Tests;

public class ApplierTests
{
    static string TempDir() { var d = Path.Combine(Path.GetTempPath(), "wa-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

    [Fact] public void Apply_swaps_new_over_current_and_launches_it()
    {
        var dir = TempDir();
        var cur = Path.Combine(dir, "App.exe"); File.WriteAllText(cur, "OLD");
        var stagedPath = Path.Combine(dir, "1.1.1-win-x64"); File.WriteAllText(stagedPath, "NEW");
        string? launched = null;
        int? code = null;
        var applier = new FileSwapApplier(p => launched = p, c => code = c);

        applier.Apply(new StagedUpdate(stagedPath, "1.1.1"), cur);

        Assert.Equal("NEW", File.ReadAllText(cur));            // current is now the new bytes
        Assert.Equal(cur, launched);                          // relaunched the same path
        Assert.Equal(0, code);                                // exit(0)
        Assert.True(File.Exists(cur + ".old"));               // old kept for next-launch cleanup
    }

    [Fact] public void Apply_restores_old_when_the_move_fails()
    {
        var dir = TempDir();
        var cur = Path.Combine(dir, "App.exe"); File.WriteAllText(cur, "OLD");
        // staged path does not exist -> the move throws -> must restore
        var applier = new FileSwapApplier(_ => { }, _ => { });
        var outcome = applier.Apply(new StagedUpdate(Path.Combine(dir, "missing"), "1.1.1"), cur);
        Assert.Equal(ApplyOutcome.Failed, outcome);
        Assert.Equal("OLD", File.ReadAllText(cur));           // original intact: "nothing happened"
        Assert.False(File.Exists(cur + ".old"));              // rename undone
    }

    [Fact] public void IsInstallDirWritable_true_for_a_temp_dir()
    {
        var dir = TempDir();
        var cur = Path.Combine(dir, "App.exe"); File.WriteAllText(cur, "x");
        Assert.True(new FileSwapApplier(_ => { }, _ => { }).IsInstallDirWritable(cur));
    }
}
