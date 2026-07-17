// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using System.Linq;

namespace Wiki.Desktop.Sync;

/// <summary>One dialable owner endpoint carried in an invite: a host plus its pairing and sync ports.</summary>
public sealed record SyncCandidate(string Host, int PairingPort, int SyncPort);

/// <summary>Codec for the invite's discovery hint. Legacy form <c>lan/&lt;host&gt;/&lt;pair&gt;/&lt;sync&gt;</c>
/// (one LAN endpoint) still parses; the multi-candidate form is <c>v2/host,pair,sync;host,pair,sync;…</c>.
/// Pure and total — <see cref="TryParse"/>/<see cref="TryParseMany"/> return false for anything malformed.</summary>
public static class SyncDiscoveryHint
{
    private const string LegacyPrefix = "lan";
    private const string ManyPrefix = "v2";

    // ---- legacy single-endpoint (kept so existing callers/tests are unaffected) ----
    public static string Format(string host, int pairingPort, int syncPort) => $"{LegacyPrefix}/{host}/{pairingPort}/{syncPort}";

    public static bool TryParse(string hint, out string host, out int pairingPort, out int syncPort)
    {
        host = ""; pairingPort = 0; syncPort = 0;
        if (string.IsNullOrEmpty(hint)) return false;
        var parts = hint.Split('/');
        if (parts.Length != 4 || parts[0] != LegacyPrefix || parts[1].Length == 0) return false;
        if (!int.TryParse(parts[2], out pairingPort) || !int.TryParse(parts[3], out syncPort)) return false;
        host = parts[1];
        return true;
    }

    // ---- multi-candidate ----
    public static string FormatMany(IReadOnlyList<SyncCandidate> candidates)
        => ManyPrefix + "/" + string.Join(";", candidates.Select(c => $"{c.Host},{c.PairingPort},{c.SyncPort}"));

    public static bool TryParseMany(string hint, out IReadOnlyList<SyncCandidate> candidates)
    {
        candidates = System.Array.Empty<SyncCandidate>();
        if (string.IsNullOrEmpty(hint)) return false;

        // Accept a legacy single-LAN hint as one candidate.
        if (TryParse(hint, out var lh, out var lp, out var ls))
        {
            candidates = new[] { new SyncCandidate(lh, lp, ls) };
            return true;
        }

        var slash = hint.IndexOf('/');
        if (slash <= 0 || hint[..slash] != ManyPrefix) return false;
        var body = hint[(slash + 1)..];
        if (body.Length == 0) return false;

        var list = new List<SyncCandidate>();
        foreach (var item in body.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
        {
            var f = item.Split(',');
            if (f.Length != 3 || f[0].Length == 0) return false;
            if (!int.TryParse(f[1], out var pair) || !int.TryParse(f[2], out var sync)) return false;
            list.Add(new SyncCandidate(f[0], pair, sync));
        }
        if (list.Count == 0) return false;
        candidates = list;
        return true;
    }
}
