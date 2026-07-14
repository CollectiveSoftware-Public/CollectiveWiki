// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Journal;

/// <summary>Resolves and creates date-stamped journal notes.</summary>
public interface IDailyNotes
{
    /// <summary>The vault-relative '/'-path of the daily note for <paramref name="date"/>.</summary>
    string ResolvePath(DateTimeOffset date);

    /// <summary>Returns today's daily-note path, creating the note (empty, or seeded from the template)
    /// if it does not already exist. Never overwrites existing content.</summary>
    string GetOrCreateToday();
}
