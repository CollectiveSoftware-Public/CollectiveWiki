// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Code.Core.Text;
using Wiki.Core.Editor;
using Wiki.Core.Parsing;

namespace Wiki.Core.Tests;

public class EditorModelOracleTests
{
    [Fact]
    public void Buffer_matches_oracle_and_plan_tracks_lines_across_many_edits()
    {
        var doc = new TextDocument("# Start\n\nbody [[Home]] and **bold**\n");
        var oracle = new StringBuilder(doc.GetText());
        var model = new EditorModel(new MarkdigMarkdownParser());
        var rng = new Random(20260628);

        const int edits = 2000;
        for (int i = 0; i < edits; i++)
        {
            if (oracle.Length > 0 && rng.Next(2) == 0)
            {
                int pos = rng.Next(oracle.Length);
                int len = Math.Min(rng.Next(1, 4), oracle.Length - pos);
                doc.Delete(pos, len);
                oracle.Remove(pos, len);
            }
            else
            {
                int pos = oracle.Length == 0 ? 0 : rng.Next(oracle.Length + 1);
                // Mix in markdown + wiki syntax + newlines so the parser is exercised under edits.
                string s = (i % 7) switch
                {
                    0 => "\n", 1 => "**x**", 2 => "[[N]]", 3 => "# ", 4 => "`c`", 5 => "- ", _ => "z"
                };
                doc.Insert(pos, s);
                oracle.Insert(pos, s);
            }

            // (1) The buffer never diverges from the oracle.
            Assert.Equal(oracle.ToString(), doc.GetText());

            // (2) The plan stays consistent with the buffer: one LineDecoration per buffer line,
            //     computed without throwing, for a caret clamped into range.
            int caret = Math.Min(rng.Next(doc.Length + 1), doc.Length);
            var plan = model.ComputePlan(doc, new SelectionSet(Selection.At(caret)));
            Assert.Equal(doc.LineCount, plan.Lines.Count);
        }
    }
}
