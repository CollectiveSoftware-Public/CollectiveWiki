// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using Wiki.Update;

namespace Wiki.Desktop.Update;

/// <summary>The app-specific constants the key-agnostic Wiki.Update library is given: the one trusted
/// signing key (public half; a list for rotation) and the GitHub releases/latest feed. No secret here.</summary>
public static class UpdateConfig
{
    public static readonly IReadOnlyList<string> TrustedKeys = new[]
    {
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAETQrOGlaKuD6QzGtUzI4E/56mEn3Dd98qXYFgRBKds+DuDYFIsbNywlsFJdzylJ7a0Ef0sXPk2srXp08A7HNqag==",
    };

    private const string Base = "https://github.com/CollectiveSoftware-Public/CollectiveWiki/releases/latest/download/";
    public static readonly Uri ManifestUrl = new(Base + "manifest.json");
    public static readonly Uri SignatureUrl = new(Base + "manifest.json.sig");

    public static UpdateFeed BuildFeed(string currentVersion, string rid, string? skipped)
        => new(ManifestUrl, SignatureUrl, TrustedKeys, currentVersion, rid, skipped);

    /// <summary>The running RID. A self-contained single-file publish reports its build RID; fall back to
    /// OS+arch so a framework-dependent dev run still yields a sensible value.</summary>
    public static string CurrentRid()
    {
        var rid = RuntimeInformation.RuntimeIdentifier;
        if (rid is "win-x64" or "linux-x64") return rid;
        var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "win-x64";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux-x64";
        return $"osx-{arch}";   // no macOS updater (D6); SelectArtifact will simply find nothing
    }

    /// <summary>The running executable's full path (what the applier swaps).</summary>
    public static string CurrentExePath() => Environment.ProcessPath
        ?? throw new InvalidOperationException("Environment.ProcessPath is null");

    public static string CurrentVersion()
        => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version is { } v
           ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
}
