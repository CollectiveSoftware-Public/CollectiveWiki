// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Parsing;

namespace Wiki.Core.Editor;

/// <summary>Public access to the internal span builder, for editor-layer consumers/tests that need the
/// flat span list without going through a full <see cref="EditorModel.ComputePlan"/> + selection.</summary>
public static class DecorationProbe
{
    public static IReadOnlyList<DecorationSpan> BuildSpans(WikiAst ast) => DecorationBuilder.Build(ast);
}
