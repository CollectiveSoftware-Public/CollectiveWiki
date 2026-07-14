// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Editor;

/// <summary>Pure word-boundary math for Ctrl+←/→, Ctrl+Backspace/Delete and double-click word
/// selection. Three character classes — word (letter/digit/_), whitespace, other — and boundaries
/// fall where the class changes. No Avalonia; unit-tested headlessly.</summary>
public static class WordNav
{
    private enum Cls { Word, Space, Other }

    private static Cls Classify(char c)
        => char.IsLetterOrDigit(c) || c == '_' ? Cls.Word
            : char.IsWhiteSpace(c) ? Cls.Space : Cls.Other;

    /// <summary>The previous word boundary: skips whitespace leftward, then the run of the class it lands on.</summary>
    public static int PrevBoundary(string text, int offset)
    {
        int i = Math.Clamp(offset, 0, text.Length);
        while (i > 0 && Classify(text[i - 1]) == Cls.Space) i--;
        if (i == 0) return 0;
        var cls = Classify(text[i - 1]);
        while (i > 0 && Classify(text[i - 1]) == cls) i--;
        return i;
    }

    /// <summary>The next word boundary: skips the run at the offset, then trailing whitespace (landing
    /// on the next word's start, editor-style).</summary>
    public static int NextBoundary(string text, int offset)
    {
        int i = Math.Clamp(offset, 0, text.Length);
        if (i >= text.Length) return text.Length;
        var cls = Classify(text[i]);
        while (i < text.Length && Classify(text[i]) == cls) i++;
        while (i < text.Length && Classify(text[i]) == Cls.Space) i++;
        return i;
    }

    /// <summary>The [start, end) run of the class at <paramref name="offset"/> — the double-click word.</summary>
    public static (int Start, int End) WordAt(string text, int offset)
    {
        if (text.Length == 0) return (0, 0);
        int i = Math.Clamp(offset, 0, text.Length - 1);
        var cls = Classify(text[i]);
        int s = i, e = i + 1;
        while (s > 0 && Classify(text[s - 1]) == cls) s--;
        while (e < text.Length && Classify(text[e]) == cls) e++;
        return (s, e);
    }
}
