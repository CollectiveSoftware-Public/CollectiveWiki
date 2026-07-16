// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace Wiki.Update.Tests;

public class ManifestParseTests
{
    const string Json = """
    {"version":"1.1.0","published":"2026-07-20T14:00:00Z",
     "notesUrl":"https://example/notes",
     "artifacts":[
       {"rid":"win-x64","url":"https://example/w.exe","sha256":"aa","size":10},
       {"rid":"linux-x64","url":"https://example/l","sha256":"bb","size":20}]}
    """;
    static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    [Fact] public void Parses_version_and_artifacts()
    {
        var m = UpdateManifest.Parse(B(Json))!;
        Assert.Equal("1.1.0", m.Version);
        Assert.Equal(2, m.Artifacts.Count);
        Assert.Equal("https://example/notes", m.NotesUrl);
    }

    [Fact] public void Selects_the_artifact_for_a_rid_by_exact_ordinal_name()
    {
        var m = UpdateManifest.Parse(B(Json))!;
        Assert.Equal("https://example/w.exe", m.SelectArtifact("win-x64")!.Url);
        Assert.Null(m.SelectArtifact("Win-X64"));    // ordinal, case-sensitive
        Assert.Null(m.SelectArtifact("osx-arm64"));  // undeclared rid -> absent
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"version\":\"1.0\"}")]            // missing artifacts
    public void Malformed_manifest_parses_to_null(string s)
        => Assert.Null(UpdateManifest.Parse(B(s)));
}
