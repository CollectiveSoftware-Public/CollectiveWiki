// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;

namespace Wiki.Editor;

/// <summary>A parsed callout header: the type (<c>note</c>…), the display title, and one of five colour
/// families the surface maps to theme brushes.</summary>
public sealed record CalloutInfo(string Type, string Title, string Family);

/// <summary>Pure detection of admonition callouts (<c>&gt; [!type] optional title</c>). Avalonia-free;
/// unit-tested.</summary>
public static class CalloutParser
{
    private static readonly Regex HeaderRx = new(@"^>\s*\[!([A-Za-z]+)\][+-]?\s*(.*)$", RegexOptions.Compiled);

    public static CalloutInfo? DetectHeader(string blockquoteLine)
    {
        var m = HeaderRx.Match(blockquoteLine);
        if (!m.Success) return null;
        string type = m.Groups[1].Value.ToLowerInvariant();
        string title = m.Groups[2].Value.Trim();
        if (title.Length == 0) title = char.ToUpperInvariant(type[0]) + type[1..];
        return new CalloutInfo(type, title, Family(type));
    }

    public static string Family(string type) => type switch
    {
        "note" or "info" or "abstract" or "summary" or "tldr" => "blue",
        "tip" or "hint" or "success" or "check" or "done" or "important" => "green",
        "warning" or "caution" or "attention" => "amber",
        "danger" or "error" or "fail" or "failure" or "missing" or "bug" => "red",
        "question" or "help" or "faq" or "example" or "quote" or "cite" => "purple",
        _ => "grey",
    };
}
