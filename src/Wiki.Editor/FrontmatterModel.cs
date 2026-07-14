// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Wiki.Editor;

public enum PropertyType { Text, List, Number, Date, Checkbox }

/// <summary>One typed frontmatter property. For <see cref="PropertyType.List"/>, <see cref="Items"/>
/// holds the values and <see cref="Value"/> is ""; otherwise <see cref="Value"/> holds the scalar
/// (Checkbox = "true"/"false") and <see cref="Items"/> is empty.</summary>
public sealed record FrontmatterProperty(string Key, PropertyType Type, string Value, IReadOnlyList<string> Items)
{
    public static FrontmatterProperty Scalar(string key, PropertyType type, string value) => new(key, type, value, Array.Empty<string>());
    public static FrontmatterProperty List(string key, IReadOnlyList<string> items) => new(key, PropertyType.List, "", items);
}

/// <summary>Typed view of a note's YAML frontmatter: <see cref="BlockLineCount"/> is how many leading
/// lines the block occupies (both fences inclusive), so <see cref="FrontmatterModel.ApplyTo"/> can splice
/// the body back on.</summary>
public sealed record FrontmatterDoc(bool HasBlock, int BlockLineCount, IReadOnlyList<FrontmatterProperty> Properties);

/// <summary>Pure parse ↔ type-infer ↔ serialize ↔ write-back for the typed Properties editor. Handles a
/// practical YAML subset: <c>key: scalar</c>, flow lists <c>key: [a, b]</c>, and block lists
/// (<c>key:</c> then <c>  - item</c>). Avalonia-free; unit-tested.</summary>
public static class FrontmatterModel
{
    private static readonly Regex KeyLine = new(@"^([^:\s][^:]*):\s?(.*)$", RegexOptions.Compiled);
    private static readonly Regex ItemLine = new(@"^\s*-\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex DateRx = new(@"^\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2})?)?$", RegexOptions.Compiled);

    public static FrontmatterDoc Parse(string noteText)
    {
        var lines = noteText.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
            return new FrontmatterDoc(false, 0, Array.Empty<FrontmatterProperty>());
        int close = -1;
        for (int i = 1; i < lines.Length; i++) if (lines[i].Trim() == "---") { close = i; break; }
        if (close < 0) return new FrontmatterDoc(false, 0, Array.Empty<FrontmatterProperty>());

        var props = new List<FrontmatterProperty>();
        int li = 1;
        while (li < close)
        {
            var m = KeyLine.Match(lines[li]);
            if (!m.Success) { li++; continue; }
            string key = m.Groups[1].Value.Trim();
            string val = m.Groups[2].Value.Trim();
            if (val.Length == 0)
            {
                var items = new List<string>();
                int j = li + 1;
                for (Match im; j < close && (im = ItemLine.Match(lines[j])).Success; j++)
                    items.Add(Unquote(im.Groups[1].Value.Trim()));
                if (items.Count > 0) { props.Add(FrontmatterProperty.List(key, items)); li = j; continue; }
                props.Add(FrontmatterProperty.Scalar(key, PropertyType.Text, "")); li++; continue;
            }
            if (val.StartsWith('[') && val.EndsWith(']'))
            {
                var items = val[1..^1].Split(',').Select(s => Unquote(s.Trim())).Where(s => s.Length > 0).ToList();
                props.Add(FrontmatterProperty.List(key, items)); li++; continue;
            }
            var type = Infer(val);
            string scalar = Unquote(val);
            if (type == PropertyType.Checkbox) scalar = scalar.ToLowerInvariant();
            props.Add(FrontmatterProperty.Scalar(key, type, scalar)); li++;
        }
        return new FrontmatterDoc(true, close + 1, props);
    }

    public static string SerializeBlock(IReadOnlyList<FrontmatterProperty> props)
    {
        if (props.Count == 0) return "";
        var sb = new StringBuilder("---\n");
        foreach (var p in props)
        {
            if (p.Type == PropertyType.List)
            {
                sb.Append(p.Key).Append(":\n");
                foreach (var it in p.Items) sb.Append("  - ").Append(QuoteIfNeeded(it)).Append('\n');
            }
            else sb.Append(p.Key).Append(": ").Append(p.Type == PropertyType.Text ? QuoteIfNeeded(p.Value) : p.Value).Append('\n');
        }
        return sb.Append("---\n").ToString();
    }

    public static string ApplyTo(string noteText, IReadOnlyList<FrontmatterProperty> props)
    {
        var doc = Parse(noteText);
        string norm = noteText.Replace("\r\n", "\n");
        string block = SerializeBlock(props);
        if (doc.HasBlock)
        {
            string body = string.Join('\n', norm.Split('\n').Skip(doc.BlockLineCount));
            return props.Count == 0 ? body : block + body;
        }
        return props.Count == 0 ? noteText : block + norm;
    }

    private static PropertyType Infer(string v)
    {
        string u = Unquote(v);
        if (u.Equals("true", StringComparison.OrdinalIgnoreCase) || u.Equals("false", StringComparison.OrdinalIgnoreCase)) return PropertyType.Checkbox;
        if (DateRx.IsMatch(u)) return PropertyType.Date;
        if (double.TryParse(u, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) return PropertyType.Number;
        return PropertyType.Text;
    }

    private static string Unquote(string s)
        => s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')) ? s[1..^1] : s;

    private static string QuoteIfNeeded(string v)
    {
        if (v.Length == 0) return "\"\"";
        bool needs = v != v.Trim() || "#:[]{}\"'>|&*!%@`,".IndexOf(v[0]) >= 0 || v.Contains(": ") || v.Contains(" #");
        return needs ? "\"" + v.Replace("\"", "\\\"") + "\"" : v;
    }
}
