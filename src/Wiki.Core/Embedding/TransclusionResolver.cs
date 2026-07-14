// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Indexing;
using Wiki.Core.Models;
using Wiki.Core.Vault;

namespace Wiki.Core.Embedding;

/// <summary>The default transclusion resolver. Images are classified by extension; note/section embeds
/// resolve through <see cref="ILinkResolver"/> and (for sections) <see cref="HeadingSectionExtractor"/>.
/// Pure over its seams.</summary>
public sealed class TransclusionResolver : ITransclusionResolver
{
    private readonly IVaultFileSystem _files;
    private readonly ILinkResolver _resolver;
    private readonly HeadingSectionExtractor _sections;

    public TransclusionResolver(IVaultFileSystem files, ILinkResolver resolver, HeadingSectionExtractor sections)
        => (_files, _resolver, _sections) = (files, resolver, sections);

    public Transclusion Resolve(WikiLink embed)
    {
        if (ImageExtensions.IsImage(embed.Target))
            return new Transclusion(TransclusionKind.Image, embed.Target, null);

        string? path = _resolver.Resolve(embed.Target);
        if (path is null) return new Transclusion(TransclusionKind.Unresolved, null, null);

        string text = _files.ReadAllText(path);
        if (embed.Heading is null)
            return new Transclusion(TransclusionKind.Note, path, text);

        var section = _sections.Extract(text, embed.Heading);
        return section is null
            ? new Transclusion(TransclusionKind.Unresolved, path, null)
            : new Transclusion(TransclusionKind.Section, path, text.Substring(section.Start, section.End - section.Start));
    }
}
