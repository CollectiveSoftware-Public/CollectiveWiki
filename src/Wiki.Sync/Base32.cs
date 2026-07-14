// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace Wiki.Sync;

/// <summary>RFC 4648 base32 encoder (lowercase, no padding). Used to render a device id as a short,
/// URL/filename-safe string from the SHA-256 of a public key. No decoder — device ids are compared,
/// pinned, and displayed as strings, never decoded back to bytes.</summary>
public static class Base32
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return "";
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = data[0];
        int bitsLeft = 8;
        int next = 1;
        while (bitsLeft > 0 || next < data.Length)
        {
            if (bitsLeft < 5)
            {
                if (next < data.Length)
                {
                    buffer = (buffer << 8) | (data[next++] & 0xff);
                    bitsLeft += 8;
                }
                else
                {
                    int pad = 5 - bitsLeft;
                    buffer <<= pad;
                    bitsLeft += pad;
                }
            }
            int index = (buffer >> (bitsLeft - 5)) & 0x1f;
            bitsLeft -= 5;
            sb.Append(Alphabet[index]);
        }
        return sb.ToString();
    }
}
