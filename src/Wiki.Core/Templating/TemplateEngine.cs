// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace Wiki.Core.Templating;

/// <summary>The default template engine. Scans for <c>{{ ... }}</c> spans and substitutes the known
/// placeholders; anything else (including malformed or unknown placeholders) is emitted verbatim.
/// Date/time formats use standard .NET format strings, evaluated with the invariant culture (the build
/// sets InvariantGlobalization), so output is stable across machines.</summary>
public sealed class TemplateEngine : ITemplateEngine
{
    private const string DefaultDateFormat = "yyyy-MM-dd";
    private const string DefaultTimeFormat = "HH:mm";

    public string Render(string template, TemplateContext context)
    {
        if (string.IsNullOrEmpty(template)) return template ?? "";
        var sb = new StringBuilder(template.Length);
        int i = 0;
        while (i < template.Length)
        {
            int open = template.IndexOf("{{", i, StringComparison.Ordinal);
            if (open < 0) { sb.Append(template, i, template.Length - i); break; }
            int close = template.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0) { sb.Append(template, i, template.Length - i); break; }

            sb.Append(template, i, open - i);                      // text before the placeholder
            string token = template.Substring(open + 2, close - (open + 2)).Trim();
            sb.Append(Substitute(token, context, original: template.Substring(open, close + 2 - open)));
            i = close + 2;
        }
        return sb.ToString();
    }

    private static string Substitute(string token, TemplateContext ctx, string original)
    {
        if (token.Equals("title", StringComparison.OrdinalIgnoreCase)) return ctx.Title;
        if (token.Equals("date", StringComparison.OrdinalIgnoreCase))
            return ctx.Now.ToString(DefaultDateFormat);
        if (token.Equals("time", StringComparison.OrdinalIgnoreCase))
            return ctx.Now.ToString(DefaultTimeFormat);
        if (TrySplitFormat(token, "date", out var dfmt)) return ctx.Now.ToString(dfmt);
        if (TrySplitFormat(token, "time", out var tfmt)) return ctx.Now.ToString(tfmt);
        return original;   // unknown placeholder — pass through unchanged
    }

    private static bool TrySplitFormat(string token, string key, out string format)
    {
        format = "";
        if (!token.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase)) return false;
        format = token[(key.Length + 1)..].Trim();
        return format.Length > 0;
    }
}
