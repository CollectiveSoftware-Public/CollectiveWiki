// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;

namespace Wiki.Core.Embedding;

/// <summary>Image size hints: the alias slot of an embed carries <c>|300</c> (display
/// width) or <c>|300x200</c> (width × height). Markdown images put the hint after the alt text
/// (<c>![photo|300](pic.png)</c>), so only the segment after the LAST pipe is considered. Anything
/// non-numeric is an ordinary alias and yields no hint.</summary>
public static class ImageSizeHint
{
    private const double Max = 10000;   // sanity cap — larger values are typos, not layout intent

    public static (double? Width, double? Height) Parse(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias)) return (null, null);
        int pipe = alias.LastIndexOf('|');
        string s = (pipe >= 0 ? alias[(pipe + 1)..] : alias).Trim();

        int x = s.IndexOfAny(['x', 'X']);
        if (x >= 0)
            return TryDim(s[..x], out var w) && TryDim(s[(x + 1)..], out var h) ? (w, h) : (null, null);
        return TryDim(s, out var only) ? (only, null) : (null, null);
    }

    private static bool TryDim(string s, out double value)
        => double.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value)
           && value > 0 && value <= Max;
}
