// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Update;

/// <summary>Everything the service needs, supplied by the app — no app-specific constant is hardcoded
/// in the module (key-agnostic, spec §5.1). TrustedKeys is a list for rotation (D10).</summary>
public sealed record UpdateFeed(
    Uri ManifestUrl,
    Uri SignatureUrl,
    IReadOnlyList<string> TrustedKeys,
    string CurrentVersion,
    string Rid,
    string? SkippedVersion);
