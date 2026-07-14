// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Vault;

namespace Wiki.Core.Tests;

/// <summary>Path-traversal containment for the on-disk vault: a relative path with <c>..</c> (or a rooted
/// path) must never resolve outside the vault root — the important case is a peer-supplied sync path being
/// flushed to disk (a paired ReadWrite peer must not be able to write outside the vault).</summary>
public class PhysicalVaultFileSystemSecurityTests
{
    private static string NewRoot()
        => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "cwiki-sec-" + Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public void Write_with_a_parent_traversal_path_is_rejected_and_nothing_escapes_the_vault()
    {
        string root = NewRoot();
        string outside = Path.Combine(Path.GetDirectoryName(root)!, "cwiki-escaped.md");
        try
        {
            var fs = new PhysicalVaultFileSystem(root);
            Assert.ThrowsAny<Exception>(() => fs.WriteAllText("../cwiki-escaped.md", "pwned"));
            Assert.False(File.Exists(outside), "traversal write escaped the vault root");
        }
        finally
        {
            if (File.Exists(outside)) File.Delete(outside);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Exists_treats_a_traversal_path_as_not_a_vault_file()
    {
        string root = NewRoot();
        try
        {
            var fs = new PhysicalVaultFileSystem(root);
            Assert.False(fs.Exists("../../anything.md"));   // safe query: escaping path is "not present"
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Normal_relative_paths_still_work()
    {
        string root = NewRoot();
        try
        {
            var fs = new PhysicalVaultFileSystem(root);
            fs.WriteAllText("sub/note.md", "ok");
            Assert.True(fs.Exists("sub/note.md"));
            Assert.Equal("ok", fs.ReadAllText("sub/note.md"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
