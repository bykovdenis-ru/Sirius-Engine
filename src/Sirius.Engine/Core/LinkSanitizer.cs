using System.Text.RegularExpressions;

namespace Sirius.Engine.Core;

/// <summary>
/// Cleans hyperlinks before they reach the UI: drops script and data schemes,
/// removes campaign parameters (utm_*, fbclid, gclid...) and normalises the host
/// so the same destination is not tracked as several different links.
/// </summary>
public static class LinkSanitizer
{
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "utm_id",
        "utm_name", "utm_reader", "utm_referrer", "utm_social", "utm_brand",
        "fbclid", "gclid", "dclid", "gclsrc", "msclkid", "yclid", "twclid",
        "mc_cid", "mc_eid", "igshid", "ttclid", "li_fat_id", "wickedid",
        "ref", "ref_src", "ref_url", "referrer", "source", "spm", "_hsenc", "_hsmi",
        "vero_id", "oly_anon_id", "oly_enc_id", "s_cid", "at_medium", "at_campaign"
    };

    private static readonly Regex TrackingPrefix = new(
        @"^(utm_|pk_|mtm_|hsa_|matomo_)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> BlockedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "javascript", "vbscript", "data", "blob", "filesystem", "chrome", "edge", "opera"
    };

    /// <summary>Returns a safe http(s) URL or null when the link cannot be used.</summary>
    public static string? Sanitize(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;

        var text = href.Trim();
        if (text.Length == 0) return null;

        var colon = text.IndexOf(':');
        if (colon > 0)
        {
            var scheme = text[..colon].Trim();
            if (BlockedSchemes.Contains(scheme)) return null;

            if (scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase) ||
                scheme.Equals("tel", StringComparison.OrdinalIgnoreCase) ||
                scheme.Equals("ftp", StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https")) return null;

        var builder = new UriBuilder(uri)
        {
            Host = uri.Host.ToLowerInvariant(),
            Fragment = uri.Fragment
        };

        if (builder.Port == 80 && uri.Scheme == "http") builder.Port = -1;
        if (builder.Port == 443 && uri.Scheme == "https") builder.Port = -1;

        builder.Query = CleanQuery(uri.Query);

        // A trailing slash on the root path adds no information.
        if (builder.Path == "/") builder.Path = string.Empty;

        var result = builder.Uri.ToString();
        return result.Length == 0 ? null : result;
    }

    public static string CleanQuery(string query)
    {
        if (string.IsNullOrEmpty(query)) return string.Empty;

        var body = query.StartsWith('?') ? query[1..] : query;
        if (body.Length == 0) return string.Empty;

        var parts = body.Split('&', StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var equals = part.IndexOf('=');
            var name = Uri.UnescapeDataString(equals < 0 ? part : part[..equals]);
            if (IsTracking(name)) continue;
            kept.Add(part);
        }

        return kept.Count == 0 ? string.Empty : "?" + string.Join("&", kept);
    }

    public static bool IsTracking(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (TrackingParameters.Contains(name)) return true;
        return TrackingPrefix.IsMatch(name);
    }

    /// <summary>Trimmed host without "www." for compact status labels.</summary>
    public static string DisplayHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;

        var host = uri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host[4..];
        return host;
    }
}
