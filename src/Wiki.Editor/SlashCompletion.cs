// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Editor;

/// <summary>Pure detection of a <c>/</c> command being typed at the start of a line (or after leading
/// whitespace) — the slash counterpart of <see cref="LinkCompletion"/>/<see cref="TagCompletion"/>.</summary>
public static class SlashCompletion
{
    public readonly record struct Context(int SlashPos, string Query);

    public static Context? Detect(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        int ls = caret <= 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        // Everything from line start to the caret must be: optional whitespace, a '/', then query chars.
        int i = ls;
        while (i < caret && (text[i] == ' ' || text[i] == '\t')) i++;
        if (i >= caret || text[i] != '/') return null;
        int slash = i;
        string query = text[(slash + 1)..caret];
        if (query.Any(char.IsWhiteSpace)) return null;   // a space ends the command
        return new Context(slash, query);
    }

    /// <summary>Removes the typed <c>/query</c> so the chosen command's edit starts from a clean line.
    /// Returns the new text and the caret position where the command should insert.</summary>
    public static (string Text, int Sel) RemoveTrigger(string text, Context ctx, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        string nt = text[..ctx.SlashPos] + text[caret..];
        return (nt, ctx.SlashPos);
    }
}
