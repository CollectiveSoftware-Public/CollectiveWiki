// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Sync;

/// <summary>Deterministic name for the "other" version kept when a concurrent edit can't be auto-merged
/// (Syncthing-style conflict-copy naming). Both peers compute the same name from the same inputs, so the conflict
/// copy converges instead of itself re-conflicting.</summary>
public static class ConflictCopyName
{
    public static string For(string path, string actor, DateTimeOffset when)
    {
        var p = path.Replace('\\', '/');
        int slash = p.LastIndexOf('/');
        string dir = slash >= 0 ? p[..slash] : "";
        string file = slash >= 0 ? p[(slash + 1)..] : p;

        int dot = file.LastIndexOf('.');
        string name = dot >= 0 ? file[..dot] : file;
        string ext = dot >= 0 ? file[dot..] : "";

        string actorShort = actor.Length <= 8 ? actor : actor[..8];
        string stamp = when.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        string conflicted = $"{name} (conflicted copy, {actorShort} {stamp}){ext}";
        return dir.Length == 0 ? conflicted : $"{dir}/{conflicted}";
    }
}
