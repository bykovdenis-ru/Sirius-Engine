namespace Sirius.Engine.Core;

/// <summary>Builds DuckDuckGo URLs for the text-friendly HTML endpoint.</summary>
public static class SearchProvider
{
    public const string Name = "DuckDuckGo";
    public const string Endpoint = "https://html.duckduckgo.com/html/";

    public static string BuildUrl(string query, string? endpoint = null)
    {
        var baseUrl = string.IsNullOrWhiteSpace(endpoint) ? Endpoint : endpoint;
        var separator = baseUrl.Contains('?') ? "&" : "?";
        return $"{baseUrl}{separator}q={Uri.EscapeDataString(query.Trim())}&kl=wt-wt";
    }

    public static string? ExtractQuery(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (!uri.Host.Contains("duckduckgo.com", StringComparison.OrdinalIgnoreCase)) return null;

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == "q")
            {
                return Uri.UnescapeDataString(parts[1]).Replace('+', ' ');
            }
        }
        return null;
    }
}
