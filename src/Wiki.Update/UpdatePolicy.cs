// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Update;

/// <summary>Pure decisions about versions and check cadence. No I/O, no ambient clock.</summary>
public static class UpdatePolicy
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    /// <summary>Semver compare: numeric core dot-parts, then prerelease rule (a version WITH a
    /// prerelease tag is LOWER than the same without). Enough for X.Y.Z and X.Y.Z-rc.N.</summary>
    public static int CompareVersions(string a, string b)
    {
        var (ca, pa) = Split(a);
        var (cb, pb) = Split(b);
        for (int i = 0; i < Math.Max(ca.Length, cb.Length); i++)
        {
            int na = i < ca.Length ? ca[i] : 0, nb = i < cb.Length ? cb[i] : 0;
            if (na != nb) return na.CompareTo(nb);
        }
        if (pa.Length == 0 && pb.Length == 0) return 0;
        if (pa.Length == 0) return 1;    // release > prerelease
        if (pb.Length == 0) return -1;
        return string.CompareOrdinal(pa, pb);
    }

    private static (int[] core, string pre) Split(string v)
    {
        v = v.Trim().TrimStart('v');
        int dash = v.IndexOf('-');
        string core = dash < 0 ? v : v[..dash];
        string pre = dash < 0 ? "" : v[(dash + 1)..];
        var parts = core.Split('.');
        var nums = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++) int.TryParse(parts[i], out nums[i]);
        return (nums, pre);
    }

    public static bool IsNewer(string candidate, string current) => CompareVersions(candidate, current) > 0;

    public static bool ShouldOffer(string candidate, string current, string? skipped)
        => IsNewer(candidate, current)
           && !string.Equals(candidate, skipped, StringComparison.Ordinal);

    public static bool IsCheckDue(DateTime? lastUtc, DateTime nowUtc)
        => lastUtc is null || nowUtc - lastUtc.Value >= CheckInterval;
}
