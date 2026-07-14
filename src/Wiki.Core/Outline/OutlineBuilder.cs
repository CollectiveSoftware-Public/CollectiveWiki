// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Outline;

/// <summary>One ATX heading in a note. <see cref="Offset"/> is the absolute source offset of the heading
/// line's start, so the editor can scroll+select it.</summary>
public sealed record OutlineHeading(int Level, string Title, int Offset);

/// <summary>Pure: extracts the ATX heading outline (`#`..`######`) from note text in document order,
/// skipping lines inside fenced code blocks. UI-free + unit-tested.</summary>
public static class OutlineBuilder
{
    public static IReadOnlyList<OutlineHeading> Build(string noteText)
    {
        var result = new List<OutlineHeading>();
        if (string.IsNullOrEmpty(noteText)) return result;

        bool inFence = false;
        foreach (var line in SplitKeepingOffsets(noteText))
        {
            string trimmed = line.Text.TrimStart();
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~")) { inFence = !inFence; continue; }
            if (inFence) continue;

            int h = 0;
            while (h < trimmed.Length && trimmed[h] == '#') h++;
            if (h is >= 1 and <= 6 && h < trimmed.Length && trimmed[h] == ' ')
            {
                string title = trimmed[(h + 1)..].Trim();
                if (title.Length > 0) result.Add(new OutlineHeading(h, title, line.Start));
            }
        }
        return result;
    }

    private readonly record struct Line(string Text, int Start);

    // Splits on '\n' while tracking each line's absolute start offset (trailing '\r' trimmed off the text).
    private static IEnumerable<Line> SplitKeepingOffsets(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n')
            {
                yield return new Line(text[start..i].TrimEnd('\r'), start);
                start = i + 1;
            }
        if (start <= text.Length)
            yield return new Line(text[start..].TrimEnd('\r'), start);
    }
}
