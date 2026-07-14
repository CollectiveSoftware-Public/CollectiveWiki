// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Parsing;

namespace Wiki.Core.Indexing;

/// <summary>Pure: rewrites <c>#old</c> (and nested <c>#old/*</c>) to <c>#new</c> across one note's text,
/// reusing the parser's code-span exclusion so a <c>#old</c> inside code/inline-code is left alone.
/// Mirrors the note-rename link-rewrite pattern; the vault driver applies it to every affected note.</summary>
public static class TagRename
{
    private static readonly MarkdigMarkdownParser Parser = new();   // stateless (static pipeline) → shareable

    public static string Rewrite(string noteText, string oldTag, string newTag)
    {
        if (string.IsNullOrEmpty(oldTag)) return noteText;
        var hits = Parser.Parse(noteText).Tags
            .Where(t => t.Name == oldTag || t.Name.StartsWith(oldTag + "/", StringComparison.Ordinal))
            .OrderByDescending(t => t.SourceStart)                  // back-to-front keeps offsets valid
            .ToList();
        string text = noteText;
        foreach (var t in hits)
        {
            string newName = newTag + t.Name[oldTag.Length..];      // preserves the nested suffix
            text = text[..t.SourceStart] + "#" + newName + text[t.SourceEnd..];
        }
        return text;
    }
}
