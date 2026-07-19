// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Desktop.ViewModels;
using Xunit;

namespace Wiki.Desktop.Tests;

/// <summary>The back/forward toolbar buttons (and Go-menu items) bind IsEnabled to CanGoBack/CanGoForward, so
/// those must mirror the window's navigation history AND raise change notifications as it moves — otherwise the
/// buttons would never enable/disable. NavigationHistory itself is covered by NavigationHistoryTests; this locks
/// the MainViewModel surface the UI actually binds to.</summary>
public class MainViewModelNavStateTests
{
    private static string MakeVault(params (string name, string text)[] notes)
    {
        string root = Path.Combine(Path.GetTempPath(), "wiki-navstate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var (name, text) in notes) File.WriteAllText(Path.Combine(root, name), text);
        return root;
    }

    [Fact]
    public async Task Nav_state_tracks_history_and_raises_change_notifications()
    {
        string root = MakeVault(("A.md", "# A\n"), ("B.md", "# B\n"));
        try
        {
            var vm = new MainViewModel();
            await vm.OpenVaultAsync(root);

            // A freshly opened vault clears history: nothing to go back or forward to.
            Assert.False(vm.CanGoBack);
            Assert.False(vm.CanGoForward);

            var changed = new List<string>();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(MainViewModel.CanGoBack) or nameof(MainViewModel.CanGoForward))
                    changed.Add(e.PropertyName!);
            };

            // Two notes visited in one tab → back becomes possible, forward does not.
            vm.OpenNote("A.md");
            vm.NavigateActiveTab("B.md");
            Assert.True(vm.CanGoBack);
            Assert.False(vm.CanGoForward);
            Assert.Contains(nameof(MainViewModel.CanGoBack), changed);   // navigation notified the bound UI

            // Back → the forward direction opens up.
            vm.GoBack();
            Assert.True(vm.CanGoForward);

            // Forward again → back to the tip: back possible, forward not.
            vm.GoForward();
            Assert.True(vm.CanGoBack);
            Assert.False(vm.CanGoForward);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
