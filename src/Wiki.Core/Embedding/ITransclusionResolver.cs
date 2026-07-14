// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Models;

namespace Wiki.Core.Embedding;

/// <summary>Resolves an embed link (<c>![[Note]]</c> / <c>![[Note#heading]]</c> / <c>![[image.png]]</c>)
/// to the content the editor should inline.</summary>
public interface ITransclusionResolver
{
    Transclusion Resolve(WikiLink embed);
}
