// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Embedding;

/// <summary>The canonical set of embeddable image extensions, shared by the transclusion resolver and
/// the editor's widget classifier (one list, not two).</summary>
public static class ImageExtensions
{
    private static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".bmp" };

    public static bool IsImage(string target)
        => Exts.Any(ext => target.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
}
