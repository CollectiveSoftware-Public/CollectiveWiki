// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Journal;

/// <summary>Daily-note configuration: the <paramref name="Folder"/> (vault-relative, "" = root), the
/// filename <paramref name="DateFormat"/> (a .NET date format), and an optional vault-relative
/// <paramref name="TemplatePath"/> used to seed a freshly created note.</summary>
public sealed record DailyNoteOptions(string Folder, string DateFormat, string? TemplatePath)
{
    public static DailyNoteOptions Default => new("", "yyyy-MM-dd", null);
}
