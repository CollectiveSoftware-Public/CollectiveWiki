// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;

namespace Wiki.Sync;

/// <summary>SHA-256 content fingerprint used for change detection in the file index. This is integrity,
/// not security — it lets a peer tell "same bytes" from "different bytes" without sending the bytes.</summary>
public static class ContentHash
{
    public static string Of(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
