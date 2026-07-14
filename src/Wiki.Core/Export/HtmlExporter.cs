// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.EmphasisExtras;

namespace Wiki.Core.Export;

/// <summary>Pure Markdown→HTML export. Rewrites wiki syntax (<c>[[link]]</c>, <c>![[embed]]</c>,
/// <c>#tag</c>) into standard markup — Markdig doesn't know it — then renders a self-contained document
/// (inline CSS, images as data: URIs). <see cref="RenderVault"/> produces a linked static site. The caller
/// supplies file access via callbacks, so this stays UI- and IO-free and unit-tested.</summary>
public static class HtmlExporter
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseYamlFrontMatter()
            .UseEmphasisExtras(EmphasisExtraOptions.Marked | EmphasisExtraOptions.Strikethrough).Build();

    private static readonly Regex Embed = new(@"!\[\[([^\]|]+)(?:\|[^\]]*)?\]\]", RegexOptions.Compiled);
    private static readonly Regex WikiLink = new(@"\[\[([^\]|]+)(?:\|([^\]]*))?\]\]", RegexOptions.Compiled);
    private static readonly Regex TagRx = new(@"(^|\s)#([A-Za-z0-9/_-]+)", RegexOptions.Compiled);

    /// <summary>Renders one note to a self-contained HTML document. <paramref name="assetDataUri"/> turns
    /// an embedded image target into a data: URI (null → the embed is dropped); <paramref name="noteHref"/>
    /// turns a wikilink target into an href (e.g. "Other.html" for a vault, "#" for a lone note).</summary>
    public static string RenderNote(string noteText, string title,
        Func<string, string?> assetDataUri, Func<string, string> noteHref)
    {
        string md = noteText.Replace("\r\n", "\n");
        md = Embed.Replace(md, m => { var uri = assetDataUri(m.Groups[1].Value.Trim()); return uri is null ? "" : $"![]({uri})"; });
        md = WikiLink.Replace(md, m => { string t = m.Groups[1].Value.Trim(); string a = m.Groups[2].Success ? m.Groups[2].Value : t; return $"[{a}]({noteHref(t)})"; });
        md = TagRx.Replace(md, m => $"{m.Groups[1].Value}<span class=\"tag\">#{m.Groups[2].Value}</span>");
        return Wrap(title, Markdown.ToHtml(md, Pipeline));
    }

    /// <summary>Renders a whole vault to a linked static site: one .html per note (inter-note wikilinks
    /// resolve to relative .html paths) plus an index.html listing every note.</summary>
    public static IReadOnlyList<(string RelPath, string Html)> RenderVault(
        IReadOnlyList<(string Path, string Text)> notes, Func<string, string?> assetDataUri)
    {
        var byTitle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in notes) byTitle[Title(n.Path)] = HtmlPath(n.Path);
        string Href(string target) => byTitle.TryGetValue(target, out var h) ? h : target + ".html";

        var outp = new List<(string, string)>();
        foreach (var n in notes) outp.Add((HtmlPath(n.Path), RenderNote(n.Text, Title(n.Path), assetDataUri, Href)));

        var idx = new StringBuilder("<h1>Vault</h1><ul>");
        foreach (var n in notes.OrderBy(n => n.Path, StringComparer.OrdinalIgnoreCase))
            idx.Append($"<li><a href=\"{Escape(HtmlPath(n.Path))}\">{Escape(Title(n.Path))}</a></li>");
        outp.Add(("index.html", Wrap("Vault", idx.Append("</ul>").ToString())));
        return outp;
    }

    /// <summary>The '/'-relative note path with its <c>.md</c> extension swapped for <c>.html</c>.</summary>
    public static string HtmlPath(string notePath)
        => (notePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? notePath[..^3] : notePath) + ".html";

    private static string Wrap(string title, string body) =>
        "<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\">" +
        $"<title>{Escape(title)}</title><style>{Css}</style></head><body><main>{body}</main></body></html>\n";

    private static string Title(string path)
    {
        int s = path.LastIndexOf('/'); string f = s < 0 ? path : path[(s + 1)..];
        return f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? f[..^3] : f;
    }

    private static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private const string Css =
        "body{font-family:system-ui,sans-serif;max-width:760px;margin:2rem auto;padding:0 1rem;line-height:1.6;color:#1a2330}" +
        "main img{max-width:100%}a{color:#0284c7}.tag{color:#0284c7;background:#e0f2fe;border-radius:4px;padding:0 4px;font-size:.9em}" +
        "code{background:#f1f5f9;padding:.1em .3em;border-radius:3px}pre{background:#f1f5f9;padding:.8em;overflow:auto;border-radius:6px}" +
        "blockquote{border-left:3px solid #cbd5e1;margin:0;padding-left:1em;color:#475569}" +
        "@media(prefers-color-scheme:dark){body{background:#0f172a;color:#e2e8f0}code,pre{background:#1e293b}.tag{background:#0c4a6e}}";
}
