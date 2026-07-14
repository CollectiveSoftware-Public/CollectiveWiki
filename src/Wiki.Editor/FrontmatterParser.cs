// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Editor;

/// <summary>A detected leading YAML front-matter block: the line range it occupies (inclusive of both
/// <c>---</c> fences) and its <c>key: value</c> entries in source order.</summary>
public sealed record FrontmatterBlock(int StartLine, int EndLine, IReadOnlyList<KeyValuePair<string, string>> Entries);

/// <summary>Pure detector for a note's leading YAML front-matter block. Source-order, list-preserving
/// (unlike the index scanner's dictionary) so the editor can render an ordered "Properties" widget and
/// know exactly which lines to hide from the body. Intentionally minimal (flat <c>key: value</c>).</summary>
public static class FrontmatterParser
{
    public static FrontmatterBlock? Parse(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var lines = text.Split('\n');
        if (lines.Length < 2 || lines[0].Trim() != "---") return null;

        int end = -1;
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---") { end = i; break; }
        }
        if (end < 0) return null;

        var entries = new List<KeyValuePair<string, string>>();
        for (int i = 1; i < end; i++)
        {
            string s = lines[i];
            if (string.IsNullOrWhiteSpace(s)) continue;
            int colon = s.IndexOf(':');
            if (colon <= 0) continue;
            entries.Add(new KeyValuePair<string, string>(s[..colon].Trim(), s[(colon + 1)..].Trim()));
        }
        return new FrontmatterBlock(0, end, entries);
    }
}
