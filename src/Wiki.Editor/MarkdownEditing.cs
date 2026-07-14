// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using System.Text.RegularExpressions;

namespace Wiki.Editor;

/// <summary>Pure markdown formatting transforms for the editor toolbar / shortcuts. Each takes the whole
/// document text plus the current selection <c>[selStart, selEnd)</c> and returns the new text with the
/// resulting selection (absolute offsets) — no Avalonia, so it is unit-tested headlessly. Inline commands
/// (bold/italic/…) toggle: wrapping an unwrapped selection, unwrapping one that is already wrapped. Line
/// commands (list/quote/heading) add their prefix to each selected line, or strip it when every line
/// already has it. The control applies the result with a single <c>TextDocument.Replace</c>.</summary>
public static class MarkdownEditing
{
    public readonly record struct FormatResult(string Text, int SelStart, int SelEnd);

    private static readonly Regex OrderedRx = new(@"^(\d+)[.)]\s", RegexOptions.Compiled);
    private static readonly Regex HeadingRx = new(@"^(#{1,6})\s+", RegexOptions.Compiled);

    // Task/bullet/ordered/quote line prefixes. The task alternative must precede the bullet one so
    // "- [ ] " isn't consumed as a bare "- ".
    internal static readonly Regex ListMarkerRx =
        new(@"^(\s*)(- \[[ xX]\] |[-*+] |\d+[.)] |> )", RegexOptions.Compiled);

    /// <summary>Wraps the selection in <paramref name="open"/>/<paramref name="close"/> (same string for
    /// symmetric emphasis like <c>**</c>). Toggles off when the selection is already wrapped — whether the
    /// delimiters sit inside the selection (<c>**bold**</c> selected) or just outside it (<c>bold</c>
    /// selected between existing <c>**</c>). An empty selection inserts the pair and puts the caret between.</summary>
    public static FormatResult Wrap(string text, int selStart, int selEnd, string open, string close)
    {
        var (s, e) = Order(text, selStart, selEnd);
        string sel = text[s..e];

        // Already wrapped, delimiters within the selection.
        if (sel.Length >= open.Length + close.Length && sel.StartsWith(open) && sel.EndsWith(close))
        {
            string inner = sel[open.Length..^close.Length];
            return new(text[..s] + inner + text[e..], s, s + inner.Length);
        }
        // Already wrapped, delimiters just outside the selection.
        if (s - open.Length >= 0 && e + close.Length <= text.Length
            && text.Substring(s - open.Length, open.Length) == open
            && text.Substring(e, close.Length) == close)
        {
            int ns = s - open.Length;
            return new(text[..ns] + sel + text[(e + close.Length)..], ns, ns + sel.Length);
        }
        // Wrap.
        string wrapped = text[..s] + open + sel + close + text[e..];
        return s == e
            ? new(wrapped, s + open.Length, s + open.Length)                 // empty → caret between
            : new(wrapped, s + open.Length, s + open.Length + sel.Length);   // keep the text selected
    }

    /// <summary>Adds <paramref name="prefix"/> (e.g. <c>"- "</c>, <c>"&gt; "</c>) to the start of each
    /// selected line, or strips it when every non-blank line already starts with it.</summary>
    public static FormatResult ToggleLinePrefix(string text, int selStart, int selEnd, string prefix)
    {
        var (bs, be) = BlockRange(text, selStart, selEnd);
        string[] lines = text[bs..be].Split('\n');
        bool anyContent = lines.Any(l => l.Trim().Length > 0);
        var content = lines.Where(l => l.Trim().Length > 0).ToList();
        bool strip = content.Count > 0 && content.All(l => l.StartsWith(prefix));

        string newBlock = string.Join('\n', lines.Select(l =>
        {
            if (anyContent && l.Trim().Length == 0) return l;                        // leave interior blanks
            if (strip) return l.StartsWith(prefix) ? l[prefix.Length..] : l;
            return prefix + l;
        }));
        return Splice(text, bs, be, newBlock);
    }

    /// <summary>Numbers each selected line <c>1. 2. 3. …</c>, or strips a leading <c>N.</c>/<c>N)</c> when
    /// every non-blank line already has one. (Live renumbering as lines are edited is out of scope.)</summary>
    public static FormatResult ToggleOrderedList(string text, int selStart, int selEnd)
    {
        var (bs, be) = BlockRange(text, selStart, selEnd);
        string[] lines = text[bs..be].Split('\n');
        bool anyContent = lines.Any(l => l.Trim().Length > 0);
        var content = lines.Where(l => l.Trim().Length > 0).ToList();
        bool strip = content.Count > 0 && content.All(l => OrderedRx.IsMatch(l));

        int n = 0;
        string newBlock = string.Join('\n', lines.Select(l =>
        {
            if (anyContent && l.Trim().Length == 0) return l;
            if (strip) { var m = OrderedRx.Match(l); return l[m.Length..]; }
            return $"{++n}. {l}";
        }));
        return Splice(text, bs, be, newBlock);
    }

    /// <summary>Sets each selected line to heading <paramref name="level"/> (1–6), replacing any existing
    /// heading marker. If the first content line is already exactly that level it clears the heading
    /// (toggle off); <paramref name="level"/> 0 always clears.</summary>
    public static FormatResult SetHeading(string text, int selStart, int selEnd, int level)
    {
        var (bs, be) = BlockRange(text, selStart, selEnd);
        string[] lines = text[bs..be].Split('\n');
        string first = lines.FirstOrDefault(l => l.Trim().Length > 0) ?? "";
        var fm = HeadingRx.Match(first);
        int effective = level > 0 && fm.Success && fm.Groups[1].Value.Length == level ? 0 : level;

        string newBlock = string.Join('\n', lines.Select(l =>
        {
            if (l.Trim().Length == 0) return l;
            var m = HeadingRx.Match(l);
            string body = m.Success ? l[m.Length..] : l;
            return effective > 0 ? new string('#', effective) + " " + body : body;
        }));
        return Splice(text, bs, be, newBlock);
    }

    /// <summary>Splices an <c>![[target]]</c> image embed into <paramref name="text"/> at the caret /
    /// selection as its own block: any selected range is dropped (like a normal paste), a leading newline is
    /// added unless the insertion point already sits at the start of a line, and a trailing newline always
    /// follows — leaving the caret on the blank line just below the embed. Block placement (its own line,
    /// caret off it) is what makes the live-preview surface draw the picture instead of revealing raw
    /// <c>![[…]]</c> text on the caret's line, so a pasted image is visible immediately.</summary>
    public static FormatResult InsertImageEmbed(string text, int selStart, int selEnd, string target)
    {
        var (s, e) = Order(text, selStart, selEnd);
        bool atLineStart = s == 0 || text[s - 1] == '\n';
        string embed = (atLineStart ? "" : "\n") + "![[" + target + "]]\n";
        int caret = s + embed.Length;
        return new(text[..s] + embed + text[e..], caret, caret);
    }

    /// <summary>Splices a GFM pipe table (header row + <c>---</c> delimiter + <paramref name="rows"/>-1 empty
    /// body rows, each <paramref name="cols"/> wide) as its own block — the same block placement as
    /// <see cref="InsertImageEmbed"/> (leading newline unless at a line start, trailing newline), leaving the
    /// caret on the blank line below. The format matches <c>MarkdownTable.TryParse</c>, so the surface draws a
    /// grid immediately.</summary>
    public static FormatResult InsertTableBlock(string text, int selStart, int selEnd, int rows, int cols)
    {
        rows = Math.Max(1, rows);
        cols = Math.Max(1, cols);
        string header = "| " + string.Join(" | ", Enumerable.Range(1, cols).Select(c => "Column " + c)) + " |";
        string delim = "| " + string.Join(" | ", Enumerable.Repeat("---", cols)) + " |";
        string body = "| " + string.Join(" | ", Enumerable.Repeat("", cols)) + " |";
        var grid = new StringBuilder().Append(header).Append('\n').Append(delim);
        for (int r = 1; r < rows; r++) grid.Append('\n').Append(body);

        var (s, e) = Order(text, selStart, selEnd);
        bool atLineStart = s == 0 || text[s - 1] == '\n';
        string block = (atLineStart ? "" : "\n") + grid + "\n";
        int caret = s + block.Length;
        return new(text[..s] + block + text[e..], caret, caret);
    }

    /// <summary>The edit for pressing Enter at <paramref name="caret"/> on a list/quote line: inserts a
    /// newline plus the continued marker (ordered items increment; tasks continue unchecked); Enter on a
    /// marker-only (empty) item strips the marker instead, ending the list. Returns null when the caret's
    /// line has no marker or the caret sits inside it — the caller inserts a plain newline.</summary>
    public static FormatResult? ContinueLine(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        int ls = caret <= 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        int nl = ls >= text.Length ? -1 : text.IndexOf('\n', ls);
        int le = nl < 0 ? text.Length : nl;
        string line = text[ls..le];
        var m = ListMarkerRx.Match(line);
        if (!m.Success || caret < ls + m.Length) return null;

        string indent = m.Groups[1].Value;
        string marker = m.Groups[2].Value;

        if (line[m.Length..].Trim().Length == 0)
        {
            // Empty item: end the list — the line becomes just its indentation.
            int c = ls + indent.Length;
            return new(text[..ls] + indent + text[le..], c, c);
        }

        string next = marker[0] == '-' && marker.Length > 2 && marker[2] == '['
            ? "- [ ] "
            : char.IsDigit(marker[0]) ? NextOrdered(marker) : marker;
        string insert = "\n" + indent + next;
        int nc = caret + insert.Length;
        return new(text[..caret] + insert + text[caret..], nc, nc);
    }

    private static string NextOrdered(string marker)
    {
        int i = 0;
        while (i < marker.Length && char.IsDigit(marker[i])) i++;
        return (int.Parse(marker[..i]) + 1) + marker[i].ToString() + " ";
    }

    private const string IndentUnit = "    ";

    /// <summary>Tab: indents the caret's list line (or every line of a multi-line selection) one unit.
    /// Null for a caret on a single non-list line — the caller keeps inserting literal spaces there.</summary>
    public static FormatResult? IndentLines(string text, int selStart, int selEnd)
    {
        var (s0, e0) = Order(text, selStart, selEnd);
        var (bs, be) = BlockRange(text, selStart, selEnd);
        string[] lines = text[bs..be].Split('\n');
        if (lines.Length == 1 && !ListMarkerRx.IsMatch(lines[0])) return null;
        string newBlock = string.Join('\n', lines.Select(l => l.Length == 0 ? l : IndentUnit + l));
        string newText = text[..bs] + newBlock + text[be..];
        return s0 == e0
            ? new(newText, s0 + IndentUnit.Length, s0 + IndentUnit.Length)
            : new(newText, bs, bs + newBlock.Length);
    }

    /// <summary>Shift+Tab: outdents the covered lines one unit (a tab or up to 4 leading spaces).
    /// Null when no covered line has leading indentation.</summary>
    public static FormatResult? OutdentLines(string text, int selStart, int selEnd)
    {
        var (s0, e0) = Order(text, selStart, selEnd);
        var (bs, be) = BlockRange(text, selStart, selEnd);
        string[] lines = text[bs..be].Split('\n');
        var stripped = lines.Select(Outdent1).ToArray();
        if (stripped.Select((l, i) => l == lines[i]).All(same => same)) return null;
        string newBlock = string.Join('\n', stripped);
        string newText = text[..bs] + newBlock + text[be..];
        if (s0 == e0)
        {
            int k = lines[0].Length - stripped[0].Length;
            int c = Math.Max(bs, s0 - k);
            return new(newText, c, c);
        }
        return new(newText, bs, bs + newBlock.Length);
    }

    private static string Outdent1(string l)
    {
        if (l.StartsWith('\t')) return l[1..];
        int n = 0;
        while (n < l.Length && n < IndentUnit.Length && l[n] == ' ') n++;
        return l[n..];
    }

    /// <summary>Wraps the selected lines in a fenced code block (```` ``` ````), selecting the inner text.</summary>
    public static FormatResult FenceBlock(string text, int selStart, int selEnd)
    {
        var (bs, be) = BlockRange(text, selStart, selEnd);
        string block = text[bs..be];
        const string open = "```\n", close = "\n```";
        string newText = text[..bs] + open + block + close + text[be..];
        int innerStart = bs + open.Length;
        return new(newText, innerStart, innerStart + block.Length);
    }

    /// <summary>The formatting a toolbar should reflect at the current caret/selection. Heading level and
    /// list kind come from the caret's line (reliable for a collapsed caret); the inline marks are detected
    /// when the delimiters sit just inside or just outside the selection (the same test <see cref="Wrap"/>
    /// uses to toggle off) — a collapsed caret can't be reliably classified without a full parse, so marks
    /// read false there. Pure, so it is unit-tested headlessly.</summary>
    public readonly record struct CaretFormat(
        bool Bold, bool Italic, bool Strike, bool Code, int HeadingLevel, bool Bullet, bool Numbered,
        bool Highlight = false);

    public static CaretFormat DetectState(string text, int selStart, int selEnd)
    {
        var (s, e) = Order(text, selStart, selEnd);
        int ls = s <= 0 ? 0 : text.LastIndexOf('\n', s - 1) + 1;
        int nl = ls >= text.Length ? -1 : text.IndexOf('\n', ls);
        int le = nl < 0 ? text.Length : nl;
        string line = text[ls..le];

        var hm = HeadingRx.Match(line);
        int heading = hm.Success ? hm.Groups[1].Value.Length : 0;

        bool bullet = false, numbered = false;
        var lm = ListMarkerRx.Match(line);
        if (lm.Success)
        {
            string marker = lm.Groups[2].Value;
            if (char.IsDigit(marker[0])) numbered = true;
            else if (marker != "> ") bullet = true;   // "- ", "* ", "+ ", "- [ ] " are bullets; "> " is a quote
        }

        bool bold = IsWrapped(text, s, e, "**");
        bool italic = !bold && IsWrapped(text, s, e, "*");   // '*' is a substring of '**'
        bool strike = IsWrapped(text, s, e, "~~");
        bool code = IsWrapped(text, s, e, "`");
        bool highlight = IsWrapped(text, s, e, "==");
        return new(bold, italic, strike, code, heading, bullet, numbered, highlight);
    }

    private static bool IsWrapped(string text, int s, int e, string delim)
    {
        string sel = text[s..e];
        if (sel.Length >= 2 * delim.Length && sel.StartsWith(delim) && sel.EndsWith(delim)) return true;
        return s - delim.Length >= 0 && e + delim.Length <= text.Length
            && text.Substring(s - delim.Length, delim.Length) == delim
            && text.Substring(e, delim.Length) == delim;
    }

    // --- helpers ---

    private static (int, int) Order(string text, int s, int e)
    {
        s = Math.Clamp(s, 0, text.Length);
        e = Math.Clamp(e, 0, text.Length);
        return s <= e ? (s, e) : (e, s);
    }

    // The whole-line span [blockStart, blockEnd) covering the selection: from the start of the line holding
    // selStart to the end (exclusive of the newline) of the line holding selEnd. A selection ending exactly
    // at a line start does not pull in that next line.
    private static (int bs, int be) BlockRange(string text, int selStart, int selEnd)
    {
        var (s, e) = Order(text, selStart, selEnd);
        int bs = s <= 0 ? 0 : text.LastIndexOf('\n', s - 1) + 1;
        int endRef = e > s && e > 0 && text[e - 1] == '\n' ? e - 1 : e;
        int nl = endRef >= text.Length ? -1 : text.IndexOf('\n', endRef);
        int be = nl < 0 ? text.Length : nl;
        return (bs, be);
    }

    private static FormatResult Splice(string text, int bs, int be, string newBlock)
        => new(text[..bs] + newBlock + text[be..], bs, bs + newBlock.Length);
}
