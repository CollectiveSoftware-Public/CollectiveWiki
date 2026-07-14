// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Avalonia.Data.Converters;

namespace Wiki.Desktop.Sync;

/// <summary>True when a tree node's name/path is a `(conflicted copy)` note, so the tree can badge it
/// (spec §10 — surface conflicts; the in-app merge view is a follow-up).</summary>
public sealed class ConflictBadgeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && ConflictCopy.IsConflictNote(s);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
