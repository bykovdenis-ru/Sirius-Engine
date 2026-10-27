using Sirius.Engine.Core;
using Sirius.Engine.Html;
using Sirius.Engine.Layout;
using Sirius.Engine.Models;
using Sirius.Engine.Serialization;

namespace Sirius.Engine.Engine;

/// <summary>High-level browser facade: navigation, history, caching and page construction.</summary>
public sealed class BrowserEngine : IDisposable
{
    public const string ProductVersion = "0.0.1";

    private readonly EngineOptions _options;
    private readonly ResourceFetcher _fetcher;
    private readonly StylesheetLoader _stylesheets;
    private readonly BrowserSession _session = new();
    private readonly Dictionary<string, PageModel> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _cacheOrder = new();
    private readonly DateTime _startedAt = DateTime.Now;

    private const int MaxCacheEntries = 24;

    public BrowserEngine(EngineOptions? options = null)
    {
        _options = options ?? new EngineOptions();
        _fetcher = new ResourceFetcher(_options);
        _stylesheets = new StylesheetLoader(_fetcher, _options);
    }

    public EngineInfo Info() => new()
    {
        Product = "Sirius",
        Version = ProductVersion,
        Engine = "Sirius.Engine",
        Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        SearchEngine = _options.SearchEngineName,
        SearchEndpoint = _options.SearchEndpoint,
        UserAgent = _options.UserAgent,
        HomeUrl = _options.HomeUrl,
        StartedAt = _startedAt
    };

    public PageResponse Navigate(string input) => NavigateInternal(input, pushHistory: true);

    public PageResponse Search(string query) => NavigateInternal(query, pushHistory: true, forceSearch: true);

    public PageResponse Home() => NavigateInternal(_options.HomeUrl, pushHistory: true);

    public PageResponse Reload()
    {
        var url = _session.Current?.Url ?? _options.HomeUrl;
        var response = Load(url, useCache: false, requested: url);
        if (response.Page is not null && _session.Current is { } entry)
        {
            _session.ReplaceCurrent(response.ResolvedUrl ?? entry.Url, response.Page.Title);
        }
        return Finalize(response, url);
    }

    public PageResponse Back()
    {
        var url = _session.Back();
        if (url is null) return EmptyState();

        var response = Load(url, useCache: true, requested: url);
        return Finalize(response, url);
    }

    public PageResponse Forward()
    {
        var url = _session.Forward();
        if (url is null) return EmptyState();

        var response = Load(url, useCache: true, requested: url);
        return Finalize(response, url);
    }

    private PageResponse NavigateInternal(string input, bool pushHistory, bool forceSearch = false)
    {
        var raw = (input ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            raw = _options.HomeUrl;
        }

        var requested = raw;
        Uri? target = null;

        if (!forceSearch && IsInternalInput(raw))
        {
            var internalUrl = NormalizeInternal(raw);
            var response = Load(internalUrl, useCache: true, requested: raw);
            if (pushHistory) _session.Push(internalUrl, response.Page?.Title ?? internalUrl);
            return Finalize(response, requested);
        }

        if (forceSearch || !UrlNormalizer.TryCreate(raw, out target) || target is null)
        {
            var searchUrl = SearchProvider.BuildUrl(raw, _options.SearchEndpoint);
            target = new Uri(searchUrl);
        }

        var page = Load(target.ToString(), useCache: false, requested: requested);
        if (pushHistory)
        {
            var finalUrl = page.ResolvedUrl ?? target.ToString();
            _session.Push(finalUrl, page.Page?.Title ?? finalUrl);
        }

        return Finalize(page, requested);
    }

    private PageResponse Load(string url, bool useCache, string requested)
    {
        if (useCache && TryGetCached(url, out var cached))
        {
            return new PageResponse
            {
                Ok = true,
                StatusCode = cached.StatusCode,
                Page = cached,
                ResolvedUrl = cached.Url
            };
        }

        if (IsInternalInput(url))
        {
            var internalUrl = NormalizeInternal(url);
            var internalPage = BuildInternalPage(internalUrl);
            StoreInCache(internalUrl, internalPage);
            return new PageResponse
            {
                Ok = true,
                StatusCode = 200,
                Page = internalPage,
                ResolvedUrl = internalUrl,
                RequestedUrl = requested
            };
        }

        FetchResult fetch;
        try
        {
            fetch = _fetcher.GetAsync(url).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return ErrorResponse(requested, ex.Message, "engine", 0);
        }

        if (!fetch.Ok && string.IsNullOrWhiteSpace(fetch.Body))
        {
            var message = string.IsNullOrWhiteSpace(fetch.Error)
                ? "Не удалось загрузить страницу."
                : fetch.Error!;
            return ErrorResponse(fetch.FinalUrl, message, fetch.ErrorKind ?? "http", fetch.StatusCode);
        }

        if (fetch.Ok && string.IsNullOrWhiteSpace(fetch.Body))
        {
            return ErrorResponse(fetch.FinalUrl, "Сервер вернул пустой ответ.", "empty", fetch.StatusCode);
        }

        PageModel page;
        try
        {
            page = BuildPage(fetch, requested);
        }
        catch (Exception ex)
        {
            return ErrorResponse(fetch.FinalUrl, "Ошибка разбора страницы: " + ex.Message, "parse", fetch.StatusCode);
        }

        var response = new PageResponse
        {
            Ok = fetch.Ok,
            StatusCode = fetch.StatusCode,
            Page = page,
            ResolvedUrl = fetch.FinalUrl,
            RequestedUrl = requested,
            Error = fetch.Ok ? null : fetch.Error,
            ErrorKind = fetch.Ok ? null : fetch.ErrorKind
        };

        StoreInCache(fetch.FinalUrl, page);
        return response;
    }

    private PageModel BuildPage(FetchResult fetch, string requested)
    {
        var finalUrl = fetch.FinalUrl;
        var uri = Uri.TryCreate(finalUrl, UriKind.Absolute, out var parsed) ? parsed : new Uri(requested);
        var info = new PageRenderInfo
        {
            FinalUrl = finalUrl,
            ContentType = fetch.ContentType,
            Charset = fetch.Charset,
            Bytes = fetch.Bytes,
            ElapsedMs = fetch.ElapsedMs,
            StatusCode = fetch.StatusCode,
            Internal = false
        };

        if (LooksLikeHtml(fetch))
        {
            var document = HtmlParser.Parse(fetch.Body);

            var searchResults = SearchResultsParser.TryExtract(document, uri, uri);
            if (searchResults is not null && searchResults.Hits.Count > 0)
            {
                return BuildSearchPage(fetch, uri, searchResults, info);
            }

            var externalCss = _stylesheets.Load(document, uri);
            return new PageBuilder(document, uri, externalCss).Build(info);
        }

        if (fetch.ContentType.StartsWith("image/", StringComparison.Ordinal))
        {
            return BuildNoticePage(
                uri,
                "Изображение",
                $"Содержимое типа {fetch.ContentType} ({FormatBytes(fetch.Bytes)}). " +
                "Просмотр изображений появится в следующих версиях Sirius.",
                info);
        }

        if (fetch.ContentType.Contains("json", StringComparison.Ordinal))
        {
            return BuildPrePage(uri, "JSON", fetch.Body, info);
        }

        if (fetch.ContentType.StartsWith("text/", StringComparison.Ordinal))
        {
            return BuildPrePage(uri, uri.Host, fetch.Body, info);
        }

        return BuildNoticePage(
            uri,
            "Неподдерживаемый формат",
            $"Сервер вернул {fetch.ContentType} ({FormatBytes(fetch.Bytes)}). " +
            "Sirius 0.0.1 отображает HTML и текст.",
            info);
    }

    private static PageModel BuildSearchPage(
        FetchResult fetch,
        Uri uri,
        SearchResultsModel results,
        PageRenderInfo info)
    {
        var page = new PageModel
        {
            Url = info.FinalUrl,
            Title = results.Query + " — " + results.Engine,
            ContentType = info.ContentType,
            Bytes = info.Bytes,
            ElapsedMs = info.ElapsedMs,
            StatusCode = info.StatusCode,
            Charset = info.Charset
        };

        page.Meta.Add(new MetaEntry { Name = "url", Value = info.FinalUrl });
        page.Meta.Add(new MetaEntry { Name = "engine", Value = results.Engine });
        page.Meta.Add(new MetaEntry { Name = "query", Value = results.Query });

        page.Blocks.Add(new BlockModel
        {
            Kind = "heading",
            Level = 1,
            SpaceAfter = 6,
            Spans = { new SpanModel { Text = results.Query } }
        });

        page.Blocks.Add(new BlockModel
        {
            Kind = "notice",
            SpaceAfter = 14,
            Spans =
            {
                new SpanModel
                {
                    Text = $"{results.Engine} · {Plural(results.Hits.Count, "результат", "результата", "результатов")}",
                    Color = "#6b7280",
                    Italic = true
                }
            }
        });

        foreach (var hit in results.Hits)
        {
            page.Blocks.Add(new BlockModel
            {
                Kind = "heading",
                Level = 2,
                SpaceAfter = 2,
                Spans =
                {
                    new SpanModel
                    {
                        Text = hit.Title,
                        Link = hit.Url,
                        Color = "#1a5ce5",
                        Bold = true
                    }
                }
            });

            page.Outline.Add(new OutlineEntry
            {
                Level = 2,
                Text = hit.Title,
                Anchor = hit.Url
            });

            if (hit.DisplayUrl.Length > 0)
            {
                page.Blocks.Add(new BlockModel
                {
                    Kind = "notice",
                    SpaceAfter = 2,
                    Spans =
                    {
                        new SpanModel
                        {
                            Text = hit.DisplayUrl,
                            Link = hit.Url,
                            Color = "#2f7d4f",
                            Italic = true
                        }
                    }
                });
            }

            if (hit.Snippet.Length > 0)
            {
                page.Blocks.Add(new BlockModel
                {
                    Kind = "paragraph",
                    SpaceAfter = 14,
                    Spans = { new SpanModel { Text = hit.Snippet, Color = "#3c4453" } }
                });
            }

            page.Links.Add(new LinkEntry { Text = hit.Title, Url = hit.Url });
        }

        if (results.Hits.Count == 0)
        {
            page.Blocks.Add(new BlockModel
            {
                Kind = "notice",
                Spans =
                {
                    new SpanModel
                    {
                        Text = "По этому запросу ничего не найдено.",
                        Italic = true,
                        Color = "#6b7280"
                    }
                }
            });
        }

        var engineLink = results.MoreUrl ?? info.FinalUrl;
        page.Blocks.Add(new BlockModel
        {
            Kind = "notice",
            Spans =
            {
                new SpanModel
                {
                    Text = "Открыть на " + results.Engine,
                    Link = engineLink,
                    Color = "#1a5ce5",
                    Italic = true
                }
            }
        });

        page.WordCount = CountWords(page.Blocks);
        return page;
    }

    private static string Plural(int count, string one, string few, string many)
    {
        var mod100 = count % 100;
        var mod10 = count % 10;
        if (mod100 is >= 11 and <= 14) return $"{count} {many}";
        if (mod10 == 1) return $"{count} {one}";
        if (mod10 is >= 2 and <= 4) return $"{count} {few}";
        return $"{count} {many}";
    }

    private static int CountWords(IEnumerable<BlockModel> blocks)
    {
        var total = 0;
        foreach (var block in blocks)
        {
            foreach (var span in block.Spans)
            {
                total += span.Text.Split(
                    new[] { ' ', '\t', '\n', '\r', '\u00A0' },
                    StringSplitOptions.RemoveEmptyEntries).Length;
            }
        }
        return total;
    }

    private static PageModel BuildInternalPage(string url)
    {
        var (html, fallbackTitle) = url switch
        {
            "sirius://about" or "about:sirius" => (InternalPages.About(), "О Sirius"),
            "sirius://blank" or "about:blank" => (InternalPages.Blank(), "Новая вкладка"),
            _ => (InternalPages.Home(), "Sirius — новая вкладка")
        };

        var page = new PageBuilder(HtmlParser.Parse(html), new Uri("sirius://internal")).Build(
            new PageRenderInfo
            {
                FinalUrl = url,
                ContentType = "text/html",
                Charset = "utf-8",
                Bytes = html.Length,
                ElapsedMs = 0,
                StatusCode = 200,
                Internal = true
            });

        if (string.IsNullOrWhiteSpace(page.Title)) page.Title = fallbackTitle;
        return page;
    }

    private static PageModel BuildPrePage(Uri uri, string title, string body, PageRenderInfo info)
    {
        const int limit = 400_000;
        var text = body.Length > limit ? body[..limit] + "\n\n… (обрезано)" : body;

        var page = new PageModel
        {
            Url = info.FinalUrl,
            Title = string.IsNullOrWhiteSpace(title) ? uri.Host : title,
            ContentType = info.ContentType,
            Bytes = info.Bytes,
            ElapsedMs = info.ElapsedMs,
            StatusCode = info.StatusCode,
            Charset = info.Charset,
            WordCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length,
            Blocks =
            {
                new BlockModel
                {
                    Kind = "pre",
                    SpaceAfter = 0,
                    Spans = { new SpanModel { Text = text } }
                }
            },
            Meta =
            {
                new MetaEntry { Name = "url", Value = info.FinalUrl },
                new MetaEntry { Name = "type", Value = info.ContentType }
            }
        };

        return page;
    }

    private static PageModel BuildNoticePage(Uri uri, string title, string message, PageRenderInfo info)
    {
        return new PageModel
        {
            Url = info.FinalUrl,
            Title = title,
            ContentType = info.ContentType,
            Bytes = info.Bytes,
            ElapsedMs = info.ElapsedMs,
            StatusCode = info.StatusCode,
            Charset = info.Charset,
            Blocks =
            {
                new BlockModel
                {
                    Kind = "heading",
                    Level = 1,
                    SpaceAfter = 12,
                    Spans = { new SpanModel { Text = title } }
                },
                new BlockModel
                {
                    Kind = "paragraph",
                    Spans = { new SpanModel { Text = message } }
                },
                new BlockModel
                {
                    Kind = "notice",
                    Spans =
                    {
                        new SpanModel
                        {
                            Text = uri.ToString(),
                            Color = "#6b7280",
                            Italic = true,
                            Link = uri.ToString()
                        }
                    }
                }
            }
        };
    }

    private PageResponse ErrorResponse(string url, string message, string kind, int statusCode)
    {
        var friendlyTitle = kind switch
        {
            "timeout" => "Сайт не отвечает",
            "network" => "Не удалось подключиться",
            "http" when statusCode == 404 => "Страница не найдена",
            "http" => "Ошибка сервера",
            "parse" => "Ошибка разбора страницы",
            _ => "Не удалось открыть страницу"
        };

        var html = InternalPages.Error(friendlyTitle, message, url, kind);
        var page = new PageBuilder(HtmlParser.Parse(html), new Uri("sirius://error")).Build(
            new PageRenderInfo
            {
                FinalUrl = url,
                ContentType = "text/html",
                Charset = "utf-8",
                Bytes = html.Length,
                ElapsedMs = 0,
                StatusCode = statusCode,
                Internal = true
            });

        page.Title = friendlyTitle;
        page.StatusCode = statusCode;

        return new PageResponse
        {
            Ok = false,
            StatusCode = statusCode,
            Page = page,
            Error = message,
            ErrorKind = kind,
            ResolvedUrl = url,
            RequestedUrl = url
        };
    }

    private PageResponse EmptyState() => new()
    {
        Ok = false,
        Error = "История пуста.",
        ErrorKind = "history",
        CanGoBack = _session.CanGoBack,
        CanGoForward = _session.CanGoForward,
        HistoryIndex = _session.Index,
        HistoryCount = _session.Count
    };

    private PageResponse Finalize(PageResponse response, string requested)
    {
        response.RequestedUrl ??= requested;
        response.CanGoBack = _session.CanGoBack;
        response.CanGoForward = _session.CanGoForward;
        response.HistoryIndex = _session.Index;
        response.HistoryCount = _session.Count;

        if (response.Page is not null && response.Page.Title.Length > 0 &&
            _session.Current is { } current &&
            string.Equals(current.Url, response.Page.Url, StringComparison.OrdinalIgnoreCase))
        {
            current.Title = response.Page.Title;
        }

        return response;
    }

    private static bool LooksLikeHtml(FetchResult fetch)
    {
        if (fetch.ContentType.Contains("html", StringComparison.OrdinalIgnoreCase)) return true;
        var start = fetch.Body.AsSpan(0, Math.Min(fetch.Body.Length, 512)).TrimStart();
        return start.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase) ||
               start.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInternalInput(string input)
    {
        var text = input.Trim();
        return text.StartsWith("sirius:", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("about:", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeInternal(string input)
    {
        var text = input.Trim().TrimEnd('/').ToLowerInvariant();
        return text switch
        {
            "about:blank" => "sirius://blank",
            "about:sirius" or "sirius://about" => "sirius://about",
            "sirius:" or "sirius://home" or "about:home" => "sirius://home",
            _ => text
        };
    }

    private bool TryGetCached(string url, out PageModel page)
    {
        if (_cache.TryGetValue(url, out page!))
        {
            _cacheOrder.Remove(url);
            _cacheOrder.AddFirst(url);
            return true;
        }
        return false;
    }

    private void StoreInCache(string url, PageModel page)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        _cache[url] = page;
        _cacheOrder.Remove(url);
        _cacheOrder.AddFirst(url);

        while (_cacheOrder.Count > MaxCacheEntries)
        {
            var last = _cacheOrder.Last;
            if (last is null) break;
            _cache.Remove(last.Value);
            _cacheOrder.RemoveLast();
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} Б";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} КБ";
        return $"{bytes / (1024.0 * 1024.0):0.0} МБ";
    }

    public void Dispose() => _fetcher.Dispose();
}
