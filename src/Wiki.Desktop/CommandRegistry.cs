// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Search;

namespace Wiki.Desktop;

/// <summary>The command-palette (Ctrl+P) command set + pure filtering. <see cref="AppCommand.Run"/> is the
/// action the head runs on Enter. Filtering reuses <see cref="QuickSwitcher.Rank"/> so the palette ranks
/// like the quick switcher (exact → prefix → substring). UI-free apart from the Action delegate — the
/// ranking is unit-tested.</summary>
public static class CommandRegistry
{
    public sealed record AppCommand(string Id, string Label, string? Gesture, Action Run);

    /// <summary>Commands matching <paramref name="query"/>, best rank first (ties alphabetical). An empty
    /// query returns every command in its original order.</summary>
    public static IReadOnlyList<AppCommand> Filter(IReadOnlyList<AppCommand> all, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return all;
        string q = query.Trim();
        return all
            .Select(c => (cmd: c, rank: QuickSwitcher.Rank(c.Label, q)))
            .Where(x => x.rank >= 0)
            .OrderBy(x => x.rank)
            .ThenBy(x => x.cmd.Label, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.cmd)
            .ToList();
    }
}
