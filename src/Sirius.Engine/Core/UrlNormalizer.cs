using System.Text.RegularExpressions;

namespace Sirius.Engine.Core;

/// <summary>
/// Turns whatever the user typed in the address bar into either an absolute URL
/// or a DuckDuckGo search query.
/// </summary>
public static class UrlNormalizer
{
    private static readonly Regex SchemePattern =
        new(@"^[a-zA-Z][a-zA-Z0-9+.\-]*:", RegexOptions.Compiled);

    private static readonly Regex DomainPattern = new(
        @"^(?:[a-zA-Z0-9](?:[a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?\.)+" +
        @"(?:[a-zA-Z]{2,63}|xn--[a-zA-Z0-9\-]{2,59})(?::\d{1,5})?(?:[/?#].*)?$",
        RegexOptions.Compiled);

    private static readonly Regex Ipv4Pattern = new(
        @"^\d{1,3}(?:\.\d{1,3}){3}(?::\d{1,5})?(?:[/?#].*)?$",
        RegexOptions.Compiled);

    private static readonly Regex LocalhostPattern = new(
        @"^(?:localhost|127\.0\.0\.1|\[::1\])(?::\d{1,5})?(?:[/?#].*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> InternalSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "sirius", "about"
    };

    /// <summary>True when the text should be treated as a URL rather than a search query.</summary>
    public static bool LooksLikeUrl(string input)
    {
        var text = (input ?? string.Empty).Trim();
        if (text.Length == 0) return false;
        if (text.Any(char.IsWhiteSpace)) return false;
        if (text.StartsWith("sirius:", StringComparison.OrdinalIgnoreCase)) return true;
        if (SchemePattern.IsMatch(text)) return true;
        if (text.StartsWith("//", StringComparison.Ordinal)) return true;
        return LocalhostPattern.IsMatch(text) || Ipv4Pattern.IsMatch(text) || DomainPattern.IsMatch(text);
    }

    /// <summary>
    /// Converts user input into an absolute <see cref="Uri"/>. Returns false when the
    /// input is not URL-like (the caller should search for it instead).
    /// </summary>
    public static bool TryCreate(string input, out Uri? uri)
    {
        uri = null;
        var text = (input ?? string.Empty).Trim();
        if (!LooksLikeUrl(text)) return false;

        if (text.StartsWith("//", StringComparison.Ordinal))
        {
            text = "https:" + text;
        }
        else if (!SchemePattern.IsMatch(text))
        {
            text = text.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
                   LocalhostPattern.IsMatch(text) || Ipv4Pattern.IsMatch(text)
                ? "http://" + text
                : "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme is not ("http" or "https" or "sirius" or "about")) return false;

        uri = parsed;
        return true;
    }

    public static bool IsInternal(Uri uri) =>
        InternalSchemes.Contains(uri.Scheme);

    /// <summary>Resolves a possibly relative href against the current page.</summary>
    public static string? ResolveHref(string? href, Uri? baseUri)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        href = Html.EntityDecoder.Decode(href.Trim());
        if (href.StartsWith('#')) return baseUri?.ToString() + href;
        if (href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) return null;
        if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return href;
        if (href.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
        if (href.StartsWith("//", StringComparison.Ordinal))
        {
            var scheme = baseUri?.Scheme ?? "https";
            return Uri.TryCreate($"{scheme}:{href}", UriKind.Absolute, out var networkPath)
                ? networkPath.ToString()
                : "https:" + href;
        }

        if (Uri.TryCreate(href, UriKind.Absolute, out var abs)) return abs.ToString();
        if (baseUri is null) return null;
        return Uri.TryCreate(baseUri, href, out var resolved) ? resolved.ToString() : null;
    }

    /// <summary>DuckDuckGo wraps outbound links in /l/?uddg=...; unwrap them for display.</summary>
    public static string UnwrapRedirect(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        if (!uri.Host.Contains("duckduckgo.com", StringComparison.OrdinalIgnoreCase)) return url;
        var query = uri.Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == "uddg")
            {
                var decoded = Uri.UnescapeDataString(parts[1]);
                if (Uri.TryCreate(decoded, UriKind.Absolute, out var target) &&
                    target.Scheme is "http" or "https")
                {
                    return target.ToString();
                }
            }
        }
        return url;
    }

    public static string Shorten(string url, int max = 90)
    {
        if (url.Length <= max) return url;
        return url[..(max - 1)] + "…";
    }
}
