using System.Globalization;
using Sirius.Engine.Html;

namespace Sirius.Engine.Css;

/// <summary>Applies the cascade and produces the small style subset the layout understands.</summary>
public sealed class StyleResolver
{
    private readonly List<CssRule> _rules;

    public StyleResolver(IEnumerable<CssRule> rules)
    {
        _rules = rules
            .OrderBy(r => r.Specificity)
            .ThenBy(r => r.Order)
            .ToList();
    }

    public ComputedStyle Resolve(HtmlElement element, ComputedStyle parent)
    {
        var style = parent.Clone();

        // Properties that are not inherited start from their initial value.
        style.DisplayNone = false;
        style.Inline = false;
        style.ListItem = false;
        style.Underline = false;
        style.Strike = false;
        style.Background = null;

        foreach (var rule in _rules)
        {
            if (rule.Matches(element)) Apply(style, rule.Declarations);
        }

        Apply(style, CssParser.ParseInlineStyle(element.GetAttribute("style")));

        if (element.HasAttribute("hidden") ||
            string.Equals(element.GetAttribute("aria-hidden"), "true", StringComparison.OrdinalIgnoreCase))
        {
            style.DisplayNone = true;
        }

        return style;
    }

    public static void Apply(ComputedStyle style, Dictionary<string, string> declarations)
    {
        foreach (var (property, rawValue) in declarations)
        {
            var value = rawValue.Trim();
            if (value.Length == 0) continue;

            switch (property)
            {
                case "display":
                {
                    var display = value.ToLowerInvariant();
                    if (display is "none") style.DisplayNone = true;
                    else if (display is "inline" or "inline-block" or "inline-flex" or "inline-grid" or "contents")
                        style.Inline = true;
                    else if (display is "list-item") style.ListItem = true;
                    break;
                }

                case "visibility":
                    if (value.StartsWith("hidden", StringComparison.OrdinalIgnoreCase) ||
                        value.StartsWith("collapse", StringComparison.OrdinalIgnoreCase))
                    {
                        style.DisplayNone = true;
                    }
                    break;

                case "color":
                {
                    var color = CssColors.Normalize(value);
                    if (color is not null) style.Color = color;
                    break;
                }

                case "background-color":
                case "background":
                {
                    var background = CssColors.Normalize(value);
                    if (background is not null) style.Background = background;
                    break;
                }

                case "font-weight":
                {
                    var weight = value.ToLowerInvariant();
                    if (weight is "bold" or "bolder") style.Bold = true;
                    else if (weight is "normal" or "lighter") style.Bold = false;
                    else if (int.TryParse(weight, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                        style.Bold = number >= 600;
                    break;
                }

                case "font-style":
                {
                    var fontStyle = value.ToLowerInvariant();
                    if (fontStyle.StartsWith("italic") || fontStyle.StartsWith("oblique")) style.Italic = true;
                    else if (fontStyle.StartsWith("normal")) style.Italic = false;
                    break;
                }

                case "text-decoration":
                case "text-decoration-line":
                {
                    var decoration = value.ToLowerInvariant();
                    style.Underline = decoration.Contains("underline");
                    style.Strike = decoration.Contains("line-through");
                    break;
                }

                case "text-align":
                {
                    var align = value.ToLowerInvariant();
                    if (align is "left" or "right" or "center" or "justify") style.Align = align;
                    break;
                }

                case "white-space":
                {
                    var whiteSpace = value.ToLowerInvariant();
                    style.Preformatted = whiteSpace is "pre" or "pre-wrap" or "break-spaces";
                    break;
                }

                case "opacity":
                {
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity) &&
                        opacity <= 0.01)
                    {
                        style.DisplayNone = true;
                    }
                    break;
                }

                case "font-size":
                {
                    if (value is "0" or "0px" or "0pt" or "0rem" or "0em") style.DisplayNone = true;
                    break;
                }
            }
        }
    }
}
