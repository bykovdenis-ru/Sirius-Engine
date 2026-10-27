using System.Collections.Concurrent;
using Sirius.Engine.Html;
using Sirius.Engine.Models;

namespace Sirius.Engine.Core;

/// <summary>
/// Loads same-origin external stylesheets referenced by &lt;link rel="stylesheet"&gt;.
/// Sirius renders text-first, so a handful of CSS files is enough to recover
/// colors, hidden elements and emphasis without downloading the whole web.
/// </summary>
public sealed class StylesheetLoader
{
    private readonly ResourceFetcher _fetcher;
    private readonly EngineOptions _options;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private sealed class CacheEntry
    {
        public string Css { get; init; } = string.Empty;
        public DateTime FetchedAt { get; init; }
    }

    public StylesheetLoader(ResourceFetcher fetcher, EngineOptions options)
    {
        _fetcher = fetcher;
        _options = options;
    }

    public string Load(HtmlDocument document, Uri pageUri)
    {
        if (!_options.LoadExternalStylesheets) return string.Empty;

        var urls = CollectUrls(document, pageUri);
        if (urls.Count == 0) return string.Empty;

        var parts = new List<string>(urls.Count);
        foreach (var url in urls)
        {
            var css = GetCss(url);
            if (!string.IsNullOrWhiteSpace(css)) parts.Add(css);
        }

        return string.Join("\n\n", parts);
    }

    private List<string> CollectUrls(HtmlDocument document, Uri pageUri)
    {
        var urls = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in document.Root.Descendants())
        {
            if (urls.Count >= Math.Max(0, _options.MaxStylesheets)) break;
            if (element.Tag != "link") continue;

            var rel = element.GetAttribute("rel");
            if (rel is null || !rel.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(r => r.Equals("stylesheet", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var media = element.GetAttribute("media")?.ToLowerInvariant() ?? string.Empty;
            if (media.Contains("print", StringComparison.Ordinal)) continue;

            var resolved = UrlNormalizer.ResolveHref(element.GetAttribute("href"), pageUri);
            if (resolved is null) continue;
            if (!Uri.TryCreate(resolved, UriKind.Absolute, out var uri)) continue;
            if (uri.Scheme is not ("http" or "https")) continue;
            if (!uri.Host.Equals(pageUri.Host, StringComparison.OrdinalIgnoreCase)) continue;
            if (!seen.Add(uri.ToString())) continue;

            urls.Add(uri.ToString());
        }

        return urls;
    }

    private string GetCss(string url)
    {
        if (_cache.TryGetValue(url, out var cached) &&
            DateTime.UtcNow - cached.FetchedAt < CacheLifetime)
        {
            return cached.Css;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = _fetcher.GetAsync(url, timeout.Token).GetAwaiter().GetResult();

            if (!result.Ok || result.Bytes > _options.MaxStylesheetBytes) return string.Empty;
            if (!LooksLikeCss(result, url)) return string.Empty;

            var css = result.Body;
            _cache[url] = new CacheEntry { Css = css, FetchedAt = DateTime.UtcNow };
            return css;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool LooksLikeCss(FetchResult result, string url)
    {
        if (result.ContentType.Contains("css", StringComparison.OrdinalIgnoreCase)) return true;
        if (url.Contains(".css", StringComparison.OrdinalIgnoreCase)) return true;

        var head = result.Body.AsSpan(0, Math.Min(result.Body.Length, 200)).TrimStart();
        return !head.StartsWith("<", StringComparison.Ordinal);
    }

    public void Clear() => _cache.Clear();
}
