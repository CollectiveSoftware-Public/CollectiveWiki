// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Vault;
using Xunit;

namespace Wiki.Core.Tests;

/// <summary>Guards the cached enumeration: <see cref="PhysicalVaultFileSystem.EnumerateMarkdownFiles"/>
/// memoizes the directory walk (so link resolution over a big vault doesn't re-walk disk per link), but
/// must still reflect files added via the file system (the cache is invalidated on write/rename).</summary>
public class PhysicalVaultFileSystemTests
{
    [Fact]
    public void Enumeration_reflects_files_added_and_renamed_through_the_fs()
    {
        string root = Path.Combine(Path.GetTempPath(), "wiki-fs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "A.md"), "a");
            var fs = new PhysicalVaultFileSystem(root);

            Assert.Equal(new[] { "A.md" }, fs.EnumerateMarkdownFiles());
            // repeated call is consistent (cache hit)
            Assert.Equal(new[] { "A.md" }, fs.EnumerateMarkdownFiles());

            // a new file written through the fs appears (cache invalidated)
            fs.WriteAllText("sub/B.md", "b");
            Assert.Equal(new[] { "A.md", "sub/B.md" }, fs.EnumerateMarkdownFiles());

            // a rename through the fs is reflected
            fs.Rename("A.md", "Z.md");
            Assert.Equal(new[] { "Z.md", "sub/B.md" }, fs.EnumerateMarkdownFiles());

            // overwriting an existing file does not drop it
            fs.WriteAllText("Z.md", "z2");
            Assert.Equal(new[] { "Z.md", "sub/B.md" }, fs.EnumerateMarkdownFiles());
            Assert.Equal("z2", fs.ReadAllText("Z.md"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateFiles_includes_assets_while_markdown_stays_notes_only()
    {
        string root = Path.Combine(Path.GetTempPath(), "wiki-fs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Files"));
        try
        {
            File.WriteAllText(Path.Combine(root, "Home.md"), "h");
            File.WriteAllText(Path.Combine(root, "Files", "pic.png"), "binary");
            var fs = new PhysicalVaultFileSystem(root);

            Assert.Equal(new[] { "Files/pic.png", "Home.md" }, fs.EnumerateFiles());
            Assert.Equal(new[] { "Home.md" }, fs.EnumerateMarkdownFiles());   // assets excluded from notes
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
