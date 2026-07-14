// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Vault;

namespace Wiki.Core.Tests;

public class VaultEnumerationTests
{
    [Fact]
    public void Dot_directories_are_not_enumerated_as_notes()
    {
        string root = Path.Combine(Path.GetTempPath(), "wiki-enum-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            Directory.CreateDirectory(Path.Combine(root, ".cwiki"));
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            File.WriteAllText(Path.Combine(root, "Home.md"), "# Home");
            File.WriteAllText(Path.Combine(root, "sub", "Nested.md"), "# Nested");
            File.WriteAllText(Path.Combine(root, ".cwiki", "cache.md"), "should be hidden");
            File.WriteAllText(Path.Combine(root, ".git", "notes.md"), "should be hidden");

            var notes = new PhysicalVaultFileSystem(root).EnumerateMarkdownFiles();

            Assert.Contains("Home.md", notes);
            Assert.Contains("sub/Nested.md", notes);
            Assert.DoesNotContain(notes, n => n.Contains(".cwiki"));
            Assert.DoesNotContain(notes, n => n.Contains(".git"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
