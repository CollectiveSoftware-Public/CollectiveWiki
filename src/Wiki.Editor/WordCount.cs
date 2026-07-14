// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Editor;

/// <summary>Pure word/character counting for the status bar (a word = a maximal non-whitespace run).</summary>
public static class WordCount
{
    public static (int Words, int Chars) Count(string text)
    {
        int words = 0;
        bool inWord = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c)) inWord = false;
            else if (!inWord) { inWord = true; words++; }
        }
        return (words, text.Length);
    }
}
