// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Desktop.ViewModels;

/// <summary>A row in the backlinks pane: the source note's display <see cref="Title"/> (its file name
/// without folder or <c>.md</c>) plus the <see cref="NotePath"/> to navigate to when clicked. Pure —
/// projected from a Core <see cref="Wiki.Core.Models.Backlink"/>.</summary>
public sealed record BacklinkRow(string Title, string NotePath)
{
    public static BacklinkRow From(Wiki.Core.Models.Backlink b)
        => new(System.IO.Path.GetFileNameWithoutExtension(b.FromNote), b.FromNote);
}
