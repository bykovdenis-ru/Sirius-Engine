namespace Sirius.Engine.Models;

/// <summary>Runtime configuration for the Sirius engine.</summary>
public sealed class EngineOptions
{
    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/124.0.0.0 Safari/537.36 Sirius/0.0.1";

    public string AcceptLanguage { get; set; } = "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7";

    public int TimeoutSeconds { get; set; } = 30;

    public int MaxResponseBytes { get; set; } = 8 * 1024 * 1024;

    public string SearchEndpoint { get; set; } = "https://html.duckduckgo.com/html/";

    public string SearchEngineName { get; set; } = "DuckDuckGo";

    public string HomeUrl { get; set; } = "sirius://home";

    /// <summary>Fetch up to <see cref="MaxStylesheets"/> same-origin CSS files.</summary>
    public bool LoadExternalStylesheets { get; set; } = true;

    public int MaxStylesheets { get; set; } = 3;

    public int MaxStylesheetBytes { get; set; } = 512 * 1024;
}

/// <summary>A single styled run of text inside a block.</summary>
public sealed class SpanModel
{
    public string Text { get; set; } = string.Empty;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Code { get; set; }
    public bool Underline { get; set; }
    public bool Strike { get; set; }
    public bool Mark { get; set; }
    public string? Link { get; set; }
    public string? Color { get; set; }
    public string? Background { get; set; }
}

/// <summary>A block-level piece of rendered content (paragraph, heading, list item...).</summary>
public sealed class BlockModel
{
    public string Kind { get; set; } = "paragraph";
    public int Level { get; set; }
    public int Indent { get; set; }
    public int SpaceBefore { get; set; }
    public int SpaceAfter { get; set; }
    public string? Align { get; set; }
    public string? Anchor { get; set; }
    public string? Marker { get; set; }
    public List<SpanModel> Spans { get; set; } = new();
}

/// <summary>Everything the UI needs to paint one document.</summary>
public sealed class PageModel
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? SiteName { get; set; }
    public string? Favicon { get; set; }
    public string? Language { get; set; }
    public string? Charset { get; set; }
    public string ContentType { get; set; } = "text/html";
    public long Bytes { get; set; }
    public int ElapsedMs { get; set; }
    public int WordCount { get; set; }
    public bool Internal { get; set; }
    public int StatusCode { get; set; } = 200;
    public List<BlockModel> Blocks { get; set; } = new();
    public List<OutlineEntry> Outline { get; set; } = new();
    public List<LinkEntry> Links { get; set; } = new();
    public List<MetaEntry> Meta { get; set; } = new();
}

public sealed class OutlineEntry
{
    public int Level { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Anchor { get; set; }
}

public sealed class LinkEntry
{
    public string Text { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public sealed class MetaEntry
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>Result of a navigation request, including session history state.</summary>
public sealed class PageResponse
{
    public bool Ok { get; set; }
    public int StatusCode { get; set; }
    public string? Error { get; set; }
    public string? ErrorKind { get; set; }
    public PageModel? Page { get; set; }
    public bool CanGoBack { get; set; }
    public bool CanGoForward { get; set; }
    public int HistoryIndex { get; set; }
    public int HistoryCount { get; set; }
    public string? RequestedUrl { get; set; }
    public string? ResolvedUrl { get; set; }
}

public sealed class SearchHit
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string DisplayUrl { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
}
