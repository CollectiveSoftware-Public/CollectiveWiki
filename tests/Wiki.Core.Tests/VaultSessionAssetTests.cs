// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Indexing;
using Wiki.Core.Parsing;
using Wiki.Core.Search;
using Wiki.Core.Workspace;
using Xunit;

namespace Wiki.Core.Tests;

public class VaultSessionAssetTests
{
    private static VaultSession New(out InMemoryVaultFileSystem fs)
    {
        fs = new InMemoryVaultFileSystem(new Dictionary<string, string> { ["Home.md"] = "# Home\n" });
        var parser = new MarkdigMarkdownParser();
        var resolver = new LinkResolver(fs, parser);
        var index = new WikiIndex(fs, parser, resolver, new InMemoryFtsIndex());
        index.Rebuild();
        return new VaultSession(fs, index, resolver);
    }

    [Fact]
    public void SaveAsset_writes_under_attachments_and_returns_a_resolvable_target()
    {
        var s = New(out var fs);

        string target = s.SaveAsset(new byte[] { 1, 2, 3, 4 }, "png");

        Assert.StartsWith("Pasted image ", target);
        Assert.EndsWith(".png", target);
        Assert.Contains(fs.EnumerateFiles(), f => f == "attachments/" + target);
        // The bare-name embed target resolves back to the file in the subfolder.
        Assert.Equal("attachments/" + target, s.ResolveAssetPath(target));
    }

    [Fact]
    public void SaveAsset_disambiguates_a_same_second_collision()
    {
        var s = New(out var fs);

        string a = s.SaveAsset(new byte[] { 1 }, "png");
        string b = s.SaveAsset(new byte[] { 2 }, "png");

        Assert.NotEqual(a, b);
        Assert.Equal(2, fs.EnumerateFiles().Count(f => f.StartsWith("attachments/")));
    }

    [Fact]
    public void SaveAsset_normalizes_a_dotted_uppercase_extension()
    {
        var s = New(out _);
        Assert.EndsWith(".jpg", s.SaveAsset(new byte[] { 1 }, ".JPG"));
    }
}
