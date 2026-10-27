using System.Text.RegularExpressions;
using Sirius.Engine.Core;
using Sirius.Engine.Html;
using Sirius.Engine.Models;

namespace Sirius.Engine.Layout;

public sealed class SearchResultsModel
{
    public string Query { get; init; } = string.Empty;
    public string Engine { get; init; } = SearchProvider.Name;
    public List<SearchHit> Hits { get; } = new();
    public string? MoreUrl { get; init; }
}

/// <summary>
/// Recognises DuckDuckGo HTML result pages and converts them into clean,
/// structured hits instead of a soup of links, timestamps and ad labels.
/// </summary>
public static class SearchResultsParser
{
    public static SearchResultsModel? TryExtract(HtmlDocument document, Uri pageUri, Uri? baseUri)
    {
        var host = pageUri.Host;
        if (!host.Contains("duckduckgo.com", StringComparison.OrdinalIgnoreCase)) return null;

        var query = SearchProvider.ExtractQuery(pageUri.ToString());
        if (string.IsNullOrWhiteSpace(query)) return null;

        var containers = new List<HtmlElement>();
        foreach (var element in document.Root.Descendants())
        {
            if (!element.HasClass("result")) continue;
            if (element.HasClass("result--ad") || element.HasClass("result--sponsored")) continue;
            if (element.HasClass("result--no-result")) continue;
            containers.Add(element);
        }

        if (containers.Count == 0) return null;

        var result = new SearchResultsModel { Query = query };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var container in containers)
        {
            var anchor = FindFirst(container,
                e => e.Tag == "a" && (e.HasClass("result__a") || e.HasClass("result-link"))) ??
                FindFirst(container, e => e.Tag == "a" && IsDuckDuckGoRedirect(e.GetAttribute("href")));

            if (anchor is null) continue;

            var href = UrlNormalizer.ResolveHref(anchor.GetAttribute("href"), baseUri);
            if (href is null) continue;

            href = UrlNormalizer.UnwrapRedirect(href);
            if (!Uri.TryCreate(href, UriKind.Absolute, out var target)) continue;
            if (target.Scheme is not ("http" or "https")) continue;
            if (!seen.Add(href)) continue;

            var title = Clean(anchor.InnerText());
            if (title.Length == 0) continue;

            var snippetElement = FindFirst(container, e => e.HasClass("result__snippet"));
            var urlElement = FindFirst(container, e => e.HasClass("result__url"));

            var display = urlElement is null ? string.Empty : Clean(urlElement.InnerText());
            if (display.Length == 0)
            {
                display = target.Host + target.AbsolutePath.TrimEnd('/');
            }

            result.Hits.Add(new SearchHit
            {
                Title = title,
                Url = href,
                DisplayUrl = display,
                Snippet = snippetElement is null ? string.Empty : Clean(snippetElement.InnerText())
            });

            if (result.Hits.Count >= 30) break;
        }

        if (result.Hits.Count == 0) return null;

        var next = FindNextPage(document, pageUri, baseUri);
        var model = new SearchResultsModel
        {
            Query = result.Query,
            MoreUrl = next
        };
        model.Hits.AddRange(result.Hits);
        return model;
    }

    private static string? FindNextPage(HtmlDocument document, Uri pageUri, Uri? baseUri)
    {
        // DuckDuckGo exposes pagination through a POST form; the HTML endpoint also
        // renders a "Next" link that carries the next offset.
        foreach (var element in document.Root.Descendants())
        {
            if (element.Tag != "a") continue;
            if (!element.HasClass("nav-link")) continue;
            var text = Clean(element.InnerText());
            if (!text.Equals("Next", StringComparison.OrdinalIgnoreCase) &&
                !text.Equals("Следующая", StringComparison.OrdinalIgnoreCase) &&
                !text.Equals("Далее", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return UrlNormalizer.ResolveHref(element.GetAttribute("href"), baseUri);
        }

        return null;
    }

    private static bool IsDuckDuckGoRedirect(string? href) =>
        href is not null && href.Contains("uddg=", StringComparison.OrdinalIgnoreCase);

    private static HtmlElement? FindFirst(HtmlElement root, Func<HtmlElement, bool> predicate) =>
        root.Descendants().FirstOrDefault(predicate);

    private static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalized = Regex.Replace(text.Replace('\u00A0', ' '), @"\s+", " ").Trim();
        return normalized.Trim('|', '·', '-', '–', '—').Trim();
    }
}
