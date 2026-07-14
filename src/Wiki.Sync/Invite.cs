// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace Wiki.Sync;

/// <summary>A paste-to-join invite (spec §7): pins the owner device id, names the vault + granted role,
/// carries a single-use pairing token + expiry, and an optional discovery hint (a LAN endpoint or the
/// relay-keyed owner id — used by the transport plan). The token is a secret the owner also retains to
/// authenticate the joiner; the collaborator sends this whole string out-of-band.</summary>
public sealed record InvitePayload(
    string OwnerDeviceId, Guid VaultId, PeerRole Role, byte[] PairingToken,
    DateTimeOffset ExpiresAt, string DiscoveryHint);

/// <summary>Encodes/parses an <see cref="InvitePayload"/> as a <c>cwiki://invite/&lt;blob&gt;</c> string.
/// The blob is a length-prefixed binary payload in URL-safe base64, so it survives copy/paste and never
/// collides with URL delimiters. <see cref="TryParse"/> is total: malformed input returns false.</summary>
public static class InviteCodec
{
    private const string Scheme = "cwiki://invite/";
    private const string Magic = "CWIKI-INV1";

    public static string Encode(InvitePayload invite)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);                              // length-prefixed UTF-8
            w.Write(invite.OwnerDeviceId);               // length-prefixed UTF-8
            w.Write(invite.VaultId.ToByteArray());       // 16 raw bytes
            w.Write((int)invite.Role);
            w.Write(invite.PairingToken.Length);
            w.Write(invite.PairingToken);
            w.Write(invite.ExpiresAt.ToUnixTimeSeconds());
            w.Write(invite.DiscoveryHint);               // length-prefixed UTF-8
        }
        return Scheme + UrlSafe(ms.ToArray());
    }

    public static bool TryParse(string text, out InvitePayload? invite)
    {
        invite = null;
        if (string.IsNullOrEmpty(text) || !text.StartsWith(Scheme, StringComparison.Ordinal)) return false;
        try
        {
            var bytes = FromUrlSafe(text[Scheme.Length..]);
            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms, Encoding.UTF8);
            if (r.ReadString() != Magic) return false;
            var owner = r.ReadString();
            var vaultId = new Guid(r.ReadBytes(16));
            var role = (PeerRole)r.ReadInt32();
            if (!Enum.IsDefined(role)) return false;
            var tokenLen = r.ReadInt32();
            if (tokenLen < 0 || tokenLen > 4096) return false;
            var token = r.ReadBytes(tokenLen);
            if (token.Length != tokenLen) return false;
            var expires = DateTimeOffset.FromUnixTimeSeconds(r.ReadInt64());
            var hint = r.ReadString();
            invite = new InvitePayload(owner, vaultId, role, token, expires, hint);
            return true;
        }
        catch
        {
            return false;   // any malformed blob (bad base64, short read, bad guid) → not an invite
        }
    }

    private static string UrlSafe(byte[] b)
        => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromUrlSafe(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        t += (t.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(t);
    }
}
