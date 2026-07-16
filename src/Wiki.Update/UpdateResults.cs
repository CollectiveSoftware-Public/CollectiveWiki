// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Update;

public sealed record UpdateInfo(string Version, ManifestArtifact Artifact, string NotesUrl);
public sealed record StagedUpdate(string FilePath, string Version);

public abstract record UpdateCheck
{
    public sealed record UpToDate : UpdateCheck;
    public sealed record Available(UpdateInfo Info) : UpdateCheck;
    public sealed record Failed(string Reason) : UpdateCheck;
}

public enum ApplyOutcome { NotWritable, Failed }   // success does not return (process restarts)
