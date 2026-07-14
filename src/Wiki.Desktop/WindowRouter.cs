// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Desktop;

/// <summary>What opening a vault should do, given the window the user acted from and the vaults already
/// open somewhere.</summary>
public enum OpenAction { InPlace, Focus, NewWindow }

/// <summary>Pure routing decision for "open a vault" (one window per vault). Unit-tested
/// without real windows.</summary>
public static class WindowRouter
{
    /// <summary>Decides how to open <paramref name="targetPath"/>: focus a window already showing it; open
    /// in the requesting window when that window has no vault yet (first run / empty state); otherwise a
    /// new window.</summary>
    /// <param name="requesterVaultRoot">The vault open in the window the user acted from (null if empty).</param>
    /// <param name="openVaultRoots">Every open window's current vault root (nulls for empty windows).</param>
    public static OpenAction Decide(string targetPath, string? requesterVaultRoot, IReadOnlyList<string?> openVaultRoots)
    {
        if (openVaultRoots.Any(r => Same(r, targetPath))) return OpenAction.Focus;   // already open somewhere
        return requesterVaultRoot is null ? OpenAction.InPlace : OpenAction.NewWindow;
    }

    /// <summary>True when two paths denote the same folder — full-path + trailing-separator normalized,
    /// case-insensitive (the app targets Windows).</summary>
    public static bool Same(string? a, string? b)
        => a is not null && b is not null && string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

    private static string Norm(string p)
    {
        try { p = Path.GetFullPath(p); } catch { /* keep the raw path if it can't be resolved */ }
        return p.TrimEnd('/', '\\');
    }
}
