using System.Globalization;
using System.Text;

namespace Sirius.Engine.Css;

public sealed class CssDeclaration
{
    public string Property { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public bool Important { get; init; }
}

public sealed class CssRule
{
    public List<CssSelector> Selectors { get; } = new();
    public Dictionary<string, string> Declarations { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Order { get; set; }
    public int Specificity => Selectors.Count == 0 ? 0 : Selectors.Max(s => s.Specificity);
    public bool Matches(Html.HtmlElement element) => Selectors.Any(s => s.Matches(element));
}

/// <summary>A computed subset of CSS used by the layout engine.</summary>
public sealed class ComputedStyle
{
    public bool DisplayNone { get; set; }
    public bool Inline { get; set; }
    public bool ListItem { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strike { get; set; }
    public bool Preformatted { get; set; }
    public string? Color { get; set; }
    public string? Background { get; set; }
    public string? Align { get; set; }

    public ComputedStyle Clone() => (ComputedStyle)MemberwiseClone();
}

public static class CssColors
{
    private static readonly Dictionary<string, string> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["transparent"] = "transparent",
        ["black"] = "#000000", ["white"] = "#ffffff", ["red"] = "#ff0000",
        ["green"] = "#008000", ["blue"] = "#0000ff", ["yellow"] = "#ffff00",
        ["orange"] = "#ffa500", ["purple"] = "#800080", ["gray"] = "#808080",
        ["grey"] = "#808080", ["silver"] = "#c0c0c0", ["maroon"] = "#800000",
        ["olive"] = "#808000", ["lime"] = "#00ff00", ["aqua"] = "#00ffff",
        ["cyan"] = "#00ffff", ["teal"] = "#008080", ["navy"] = "#000080",
        ["fuchsia"] = "#ff00ff", ["magenta"] = "#ff00ff", ["pink"] = "#ffc0cb",
        ["brown"] = "#a52a2a", ["gold"] = "#ffd700", ["coral"] = "#ff7f50",
        ["crimson"] = "#dc143c", ["indigo"] = "#4b0082", ["violet"] = "#ee82ee",
        ["salmon"] = "#fa8072", ["tomato"] = "#ff6347", ["khaki"] = "#f0e68c",
        ["lavender"] = "#e6e6fa", ["beige"] = "#f5f5dc", ["ivory"] = "#fffff0",
        ["snow"] = "#fffafa", ["azure"] = "#f0ffff", ["linen"] = "#faf0e6",
        ["wheat"] = "#f5deb3", ["plum"] = "#dda0dd", ["orchid"] = "#da70d6",
        ["tan"] = "#d2b48c", ["chocolate"] = "#d2691e", ["firebrick"] = "#b22222",
        ["darkred"] = "#8b0000", ["darkgreen"] = "#006400", ["darkblue"] = "#00008b",
        ["darkgray"] = "#a9a9a9", ["darkgrey"] = "#a9a9a9", ["lightgray"] = "#d3d3d3",
        ["lightgrey"] = "#d3d3d3", ["dimgray"] = "#696969", ["dimgrey"] = "#696969",
        ["whitesmoke"] = "#f5f5f5", ["gainsboro"] = "#dcdcdc", ["rebeccapurple"] = "#663399",
        ["steelblue"] = "#4682b4", ["royalblue"] = "#4169e1", ["dodgerblue"] = "#1e90ff",
        ["skyblue"] = "#87ceeb", ["deepskyblue"] = "#00bfff", ["seagreen"] = "#2e8b57",
        ["forestgreen"] = "#228b22", ["limegreen"] = "#32cd32", ["darkorange"] = "#ff8c00"
    };

    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = raw.Trim().TrimEnd(';');

        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return "transparent";
        if (value.Equals("currentcolor", StringComparison.OrdinalIgnoreCase)) return null;

        if (value.StartsWith('#'))
        {
            var hex = value[1..];
            if (hex.Length == 3)
            {
                var expanded = string.Concat(hex.Select(c => new string(c, 2)));
                return "#" + expanded.ToLowerInvariant();
            }
            if (hex.Length is 6 or 8 && hex.All(Uri.IsHexDigit))
            {
                return "#" + hex[..6].ToLowerInvariant();
            }
            return null;
        }

        if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var open = value.IndexOf('(');
            var close = value.IndexOf(')');
            if (open < 0 || close < open) return null;

            var body = value[(open + 1)..close]
                .Split(new[] { ',', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (body.Length < 3) return null;
            var channels = new int[3];
            for (var i = 0; i < 3; i++)
            {
                var part = body[i].Trim();
                if (part.EndsWith('%') &&
                    double.TryParse(part.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                {
                    channels[i] = (int)Math.Round(pct * 2.55);
                }
                else if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var channel))
                {
                    channels[i] = channel;
                }
                else return null;
            }

            return $"#{Math.Clamp(channels[0], 0, 255):x2}" +
                   $"{Math.Clamp(channels[1], 0, 255):x2}" +
                   $"{Math.Clamp(channels[2], 0, 255):x2}";
        }

        return Named.TryGetValue(value, out var named) ? named : null;
    }
}

public static class CssParser
{
    public static List<CssRule> ParseStylesheet(string? css)
    {
        var rules = new List<CssRule>();
        if (string.IsNullOrWhiteSpace(css)) return rules;

        var text = StripComments(css);
        var order = 0;
        var i = 0;

        while (i < text.Length)
        {
            var brace = text.IndexOf('{', i);
            if (brace < 0) break;

            var prelude = text[i..brace].Trim();
            var close = FindMatchingBrace(text, brace);
            if (close < 0) close = text.Length;

            var body = text[(brace + 1)..close];

            if (prelude.StartsWith('@'))
            {
                var name = prelude[1..].Split(' ', '\t', '\n', '\r', '(', '{')[0].ToLowerInvariant();
                if (name is "media" or "supports" or "layer" or "container")
                {
                    if (name == "media" && !MediaQuery.MatchesScreen(MediaQueryText(prelude)))
                    {
                        i = close + 1;
                        continue;
                    }

                    var nested = ParseStylesheet(body);
                    foreach (var nestedRule in nested)
                    {
                        nestedRule.Order = order++;
                        rules.Add(nestedRule);
                    }
                }
                // @font-face, @keyframes, @import, @charset are intentionally ignored.
            }
            else
            {
                var declarations = ParseDeclarations(body);
                if (declarations.Count == 0)
                {
                    i = close + 1;
                    continue;
                }

                var selectors = CssSelector.ParseList(prelude);
                if (selectors.Count > 0)
                {
                    var rule = new CssRule { Order = order++ };
                    rule.Selectors.AddRange(selectors);
                    foreach (var pair in declarations) rule.Declarations[pair.Key] = pair.Value;
                    rules.Add(rule);
                }
            }

            i = close + 1;
        }

        return rules;
    }

    private static string MediaQueryText(string prelude)
    {
        var space = prelude.IndexOfAny(new[] { ' ', '\t', '\n', '\r' });
        return space < 0 ? string.Empty : prelude[(space + 1)..].Trim();
    }

    public static Dictionary<string, string> ParseInlineStyle(string? style)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(style)) return result;

        foreach (var declaration in ParseDeclarationList(style))
        {
            result.TryAdd(declaration.Property, declaration.Value);
        }
        return result;
    }

    private static Dictionary<string, string> ParseDeclarations(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in ParseDeclarationList(body))
        {
            result[declaration.Property] = declaration.Value;
        }
        return result;
    }

    private static IEnumerable<CssDeclaration> ParseDeclarationList(string body)
    {
        foreach (var raw in SplitTopLevel(body, ';'))
        {
            var entry = raw.Trim();
            if (entry.Length == 0) continue;

            var colon = entry.IndexOf(':');
            if (colon <= 0) continue;

            var property = entry[..colon].Trim().ToLowerInvariant();
            var value = entry[(colon + 1)..].Trim();
            if (property.Length == 0 || value.Length == 0) continue;

            var important = false;
            if (value.EndsWith("!important", StringComparison.OrdinalIgnoreCase))
            {
                important = true;
                value = value[..^10].Trim();
            }

            yield return new CssDeclaration { Property = property, Value = value, Important = important };
        }
    }

    private static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        var depth = 0;
        var quote = '\0';
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                continue;
            }

            switch (c)
            {
                case '"' or '\'':
                    quote = c;
                    break;
                case '(' or '[':
                    depth++;
                    break;
                case ')' or ']':
                    depth = Math.Max(0, depth - 1);
                    break;
                case var _ when c == separator && depth == 0:
                    parts.Add(text[start..i]);
                    start = i + 1;
                    break;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }

    private static int FindMatchingBrace(string text, int open)
    {
        var depth = 0;
        var quote = '\0';
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote && text[i - 1] != '\\') quote = '\0';
                continue;
            }

            if (c is '"' or '\'') quote = c;
            else if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static string StripComments(string css)
    {
        var sb = new StringBuilder(css.Length);
        var i = 0;
        while (i < css.Length)
        {
            if (css[i] == '/' && i + 1 < css.Length && css[i + 1] == '*')
            {
                var end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? css.Length : end + 2;
            }
            else
            {
                sb.Append(css[i]);
                i++;
            }
        }
        return sb.ToString();
    }
}
