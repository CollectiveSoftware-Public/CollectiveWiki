// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Workspace;
using Xunit;

namespace Wiki.Core.Tests;

public class VaultWorkspaceTests
{
    // VaultWorkspace.Open is the heavy "open a vault" work (build index + session) extracted into a
    // UI-free, synchronous unit so the desktop head can run it on a background thread (Task.Run) and
    // never block the UI thread — the fix for the large-vault open hang.
    [Fact]
    public void Open_builds_a_session_listing_the_vault_notes()
    {
        string root = Path.Combine(Path.GetTempPath(), "wiki-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "Alpha.md"), "# Alpha\nlinks [[Beta]]\n");
            File.WriteAllText(Path.Combine(root, "Beta.md"), "# Beta\n");

            VaultWorkspace.OpenResult result = VaultWorkspace.Open(root);

            Assert.NotNull(result.Session);
            Assert.NotNull(result.DailyNotes);
            Assert.Equal(new[] { "Alpha.md", "Beta.md" }, result.Notes);
            // the session is wired to a rebuilt index: backlinks resolve
            Assert.Contains(result.Session.Backlinks("Beta.md"), b => b.FromNote == "Alpha.md");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
