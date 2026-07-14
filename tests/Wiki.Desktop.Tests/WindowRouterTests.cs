// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Desktop;
using Xunit;

namespace Wiki.Desktop.Tests;

public class WindowRouterTests
{
    [Fact]
    public void Empty_requester_opens_in_place()
    {
        var action = WindowRouter.Decide(@"C:\Vaults\Notes", requesterVaultRoot: null, openVaultRoots: [null]);
        Assert.Equal(OpenAction.InPlace, action);
    }

    [Fact]
    public void Different_vault_opens_a_new_window()
    {
        var action = WindowRouter.Decide(@"C:\Vaults\Other", @"C:\Vaults\Notes", [@"C:\Vaults\Notes"]);
        Assert.Equal(OpenAction.NewWindow, action);
    }

    [Fact]
    public void Already_open_vault_focuses_its_window()
    {
        var action = WindowRouter.Decide(@"C:\Vaults\Other", @"C:\Vaults\Notes",
            [@"C:\Vaults\Notes", @"C:\Vaults\Other"]);
        Assert.Equal(OpenAction.Focus, action);
    }

    [Fact]
    public void Reopening_the_requesters_own_vault_focuses_rather_than_reloads()
    {
        var action = WindowRouter.Decide(@"C:\Vaults\Notes", @"C:\Vaults\Notes", [@"C:\Vaults\Notes"]);
        Assert.Equal(OpenAction.Focus, action);
    }

    [Fact]
    public void Focus_wins_even_when_the_requester_is_empty()
    {
        // An empty window opening a vault already shown elsewhere focuses that one (avoids a duplicate).
        var action = WindowRouter.Decide(@"C:\Vaults\Notes", requesterVaultRoot: null,
            openVaultRoots: [null, @"C:\Vaults\Notes"]);
        Assert.Equal(OpenAction.Focus, action);
    }

    [Fact]
    public void Same_normalizes_paths()
    {
        // Build paths from the running OS's own root/separators so Path.GetFullPath normalizes them
        // identically on Windows, Linux, and macOS. (Windows-only literals such as @"C:\Vaults\Sub\..\Notes"
        // only collapse the "..\" segment on Windows, where a backslash is a path separator.)
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Vaults", "Notes"));
        var parent = Path.GetDirectoryName(root)!;

        Assert.True(WindowRouter.Same(root, root + Path.DirectorySeparatorChar));         // trailing separator
        Assert.True(WindowRouter.Same(root, root.ToUpperInvariant()));                     // case-insensitive (targets Windows)
        Assert.True(WindowRouter.Same(root, Path.Combine(parent, "Sub", "..", "Notes")));  // relative segment collapses
        Assert.False(WindowRouter.Same(root, Path.Combine(parent, "Elsewhere")));          // genuinely different folder
    }
}
