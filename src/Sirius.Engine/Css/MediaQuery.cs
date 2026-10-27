using System.Globalization;

namespace Sirius.Engine.Css;

/// <summary>
/// Facts about the virtual screen Sirius renders for. The text-first layout has
/// no real viewport, so these values describe the reading column rather than a
/// physical window: a wide desktop "screen" with a light color scheme.
/// </summary>
public sealed class MediaContext
{
    public int Width { get; init; } = 1024;
    public int Height { get; init; } = 768;
    public double PixelRatio { get; init; } = 1.0;
    public string ColorScheme { get; init; } = "light";
    public string Orientation { get; init; } = "landscape";
    public bool Hover { get; init; } = true;
    public bool Touch { get; init; }

    public static MediaContext Default { get; } = new();
}

/// <summary>
/// A small media-query evaluator. Sirius keeps only rules that apply to a
/// desktop reading surface, which removes print-only sheets, dark-mode
/// overrides and phone breakpoints that would otherwise fight each other.
/// </summary>
public static class MediaQuery
{
    private const double BaseFontSize = 16.0;

    /// <summary>True when at least one comma-separated query matches the context.</summary>
    public static bool MatchesScreen(string? query, MediaContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;

        var ctx = context ?? MediaContext.Default;
        foreach (var raw in SplitQueries(query))
        {
            if (MatchesSingle(raw, ctx)) return true;
        }
        return false;
    }

    private static bool MatchesSingle(string raw, MediaContext context)
    {
        var query = raw.Trim();
        if (query.Length == 0) return true;

        var negated = false;
        if (query.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
        {
            negated = true;
            query = query[4..].Trim();
        }
        else if (query.StartsWith("only ", StringComparison.OrdinalIgnoreCase))
        {
            query = query[5..].Trim();
        }

        var features = ExtractFeatures(query);
        var mediaType = StripFeatures(query).Trim().ToLowerInvariant();

        var typeMatches = mediaType.Length == 0 || mediaType switch
        {
            "all" or "screen" => true,
            "print" or "speech" or "aural" or "braille" or "embossed" or "handheld" or "projection" or "tty" or "tv" => false,
            _ => true
        };

        var result = typeMatches;
        foreach (var feature in features)
        {
            if (!EvaluateFeature(feature, context))
            {
                result = false;
                break;
            }
        }

        return negated ? !result : result;
    }

    private static IEnumerable<string> SplitQueries(string query)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < query.Length; i++)
        {
            var c = query[i];
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (c == ',' && depth == 0)
            {
                yield return query[start..i];
                start = i + 1;
            }
        }
        yield return query[start..];
    }

    private static List<string> ExtractFeatures(string query)
    {
        var features = new List<string>();
        var depth = 0;
        var start = -1;
        for (var i = 0; i < query.Length; i++)
        {
            var c = query[i];
            if (c == '(')
            {
                if (depth == 0) start = i + 1;
                depth++;
            }
            else if (c == ')')
            {
                depth = Math.Max(0, depth - 1);
                if (depth == 0 && start >= 0)
                {
                    features.Add(query[start..i].Trim());
                    start = -1;
                }
            }
        }
        return features;
    }

    private static string StripFeatures(string query)
    {
        var result = new System.Text.StringBuilder(query.Length);
        var depth = 0;
        foreach (var c in query)
        {
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (depth == 0) result.Append(c);
        }
        return result.ToString();
    }

    private static bool EvaluateFeature(string feature, MediaContext context)
    {
        if (feature.Length == 0) return true;

        var colon = feature.IndexOf(':');
        var name = (colon < 0 ? feature : feature[..colon]).Trim().ToLowerInvariant();
        var value = colon < 0 ? string.Empty : feature[(colon + 1)..].Trim().ToLowerInvariant();

        switch (name)
        {
            case "min-width":
                return context.Width >= Length(value, context);
            case "max-width":
                return context.Width <= Length(value, context);
            case "width":
                return Math.Abs(context.Width - Length(value, context)) <= 1;
            case "min-height":
                return context.Height >= Length(value, context);
            case "max-height":
                return context.Height <= Length(value, context);
            case "height":
                return Math.Abs(context.Height - Length(value, context)) <= 1;
            case "min-device-width":
                return context.Width >= Length(value, context);
            case "max-device-width":
                return context.Width <= Length(value, context);
            case "orientation":
                return value.Length == 0 ||
                       value.Equals(context.Orientation, StringComparison.OrdinalIgnoreCase);
            case "prefers-color-scheme":
                return value.Length == 0 ||
                       value.Equals(context.ColorScheme, StringComparison.OrdinalIgnoreCase);
            case "hover":
                return value != "hover" || context.Hover;
            case "pointer" or "any-pointer":
                return value != "fine" || !context.Touch;
            case "prefers-reduced-motion":
                // A text renderer has no animation, so both answers are safe.
                return true;
            case "-webkit-min-device-pixel-ratio":
            case "min-resolution":
                return true;
            case "max-resolution":
                return true;
            default:
                // Unknown or unsupported feature: do not discard the rule, the
                // declarations inside may still be useful for a text layout.
                return true;
        }
    }

    private static double Length(string value, MediaContext context)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;

        var text = value.Trim();

        if (text.EndsWith("em", StringComparison.OrdinalIgnoreCase))
        {
            return ParseNumber(text[..^2]) * BaseFontSize;
        }
        if (text.EndsWith("rem", StringComparison.OrdinalIgnoreCase))
        {
            return ParseNumber(text[..^3]) * BaseFontSize;
        }
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            return ParseNumber(text[..^2]);
        }
        if (text.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
        {
            return ParseNumber(text[..^2]) * 4.0 / 3.0;
        }
        if (text.EndsWith("vw", StringComparison.OrdinalIgnoreCase))
        {
            return ParseNumber(text[..^2]) * context.Width / 100.0;
        }
        if (text.EndsWith("vh", StringComparison.OrdinalIgnoreCase))
        {
            return ParseNumber(text[..^2]) * context.Height / 100.0;
        }

        return ParseNumber(text);
    }

    private static double ParseNumber(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
}
