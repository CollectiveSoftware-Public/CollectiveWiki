// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Desktop.ViewModels;
using Xunit;

namespace Wiki.Desktop.Tests;

public class MainViewModelVaultSwitchTests
{
    private static string MakeVault(params (string name, string text)[] notes)
    {
        string root = Path.Combine(Path.GetTempPath(), "wiki-vaultswitch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var (name, text) in notes)
            File.WriteAllText(Path.Combine(root, name), text);
        return root;
    }

    [Fact]
    public async Task Opening_a_second_vault_replaces_the_first_vaults_tabs()
    {
        string a = MakeVault(("Alpha.md", "# Alpha\n"));
        string b = MakeVault(("Beta.md", "# Beta\n"));
        try
        {
            var vm = new MainViewModel();

            await vm.OpenVaultAsync(a);
            Assert.Contains(vm.Tabs.Tabs, t => t.NotePath == "Alpha.md");

            await vm.OpenVaultAsync(b);

            // The old vault's tab must be gone; only the new vault's note remains open.
            Assert.DoesNotContain(vm.Tabs.Tabs, t => t.NotePath == "Alpha.md");
            Assert.Contains(vm.Tabs.Tabs, t => t.NotePath == "Beta.md");
        }
        finally
        {
            Directory.Delete(a, recursive: true);
            Directory.Delete(b, recursive: true);
        }
    }
}
