// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Desktop.Sync;

/// <summary>Codec for the invite's discovery hint — the owner's LAN endpoint as
/// <c>lan/&lt;host&gt;/&lt;pairingPort&gt;/&lt;syncPort&gt;</c>. Pure and total: <see cref="TryParse"/>
/// returns false for anything malformed. The joiner reads this to know where to dial for pairing + sync.</summary>
public static class SyncDiscoveryHint
{
    private const string Prefix = "lan";

    public static string Format(string host, int pairingPort, int syncPort) => $"{Prefix}/{host}/{pairingPort}/{syncPort}";

    public static bool TryParse(string hint, out string host, out int pairingPort, out int syncPort)
    {
        host = ""; pairingPort = 0; syncPort = 0;
        if (string.IsNullOrEmpty(hint)) return false;
        var parts = hint.Split('/');
        if (parts.Length != 4 || parts[0] != Prefix || parts[1].Length == 0) return false;
        if (!int.TryParse(parts[2], out pairingPort) || !int.TryParse(parts[3], out syncPort)) return false;
        host = parts[1];
        return true;
    }
}
