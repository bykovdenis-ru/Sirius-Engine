using System.Text;
using System.Text.RegularExpressions;
using Sirius.Engine.Core;
using Sirius.Engine.Css;
using Sirius.Engine.Html;
using Sirius.Engine.Models;

namespace Sirius.Engine.Layout;

/// <summary>
/// Converts a parsed DOM tree into a flat list of styled blocks that any UI
/// (in our case the Tkinter front-end) can paint without knowing HTML.
/// </summary>
public sealed class PageBuilder
{
    private static readonly HashSet<string> SkipTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "head", "title", "meta", "link", "base", "noscript", "template",
        "form", "input", "button", "select", "option", "optgroup", "textarea", "iframe",
        "object", "embed", "svg", "canvas", "audio", "video", "source", "track", "map",
        "nav", "footer", "dialog"
    };

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "html", "body", "div", "section", "article", "main", "aside", "header", "address",
        "figure", "figcaption", "details", "summary", "fieldset", "legend", "dl", "dt", "dd",
        "center", "table", "thead", "tbody", "tfoot", "caption", "hgroup", "search", "p",
        "listing", "plaintext", "xmp"
    };

    private static readonly HashSet<string> HeadingTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "h1", "h2", "h3", "h4", "h5", "h6"
    };

    private readonly HtmlDocument _document;
    private readonly Uri _pageUri;
    private readonly Uri? _baseUri;
    private readonly StyleResolver _resolver;
    private readonly PageModel _page = new();
    private readonly List<BlockModel> _blocks = new();
    private readonly List<OutlineEntry> _outline = new();
    private readonly List<LinkEntry> _links = new();
    private readonly HashSet<string> _linkKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenMeta = new(StringComparer.OrdinalIgnoreCase);

    private BlockModel? _run;
    private bool _lastWasSpace = true;
    private string? _link;
    private int _quoteDepth;
    private int _headingLevel;
    private readonly List<ListState> _lists = new();
    private readonly Stack<int> _rowCells = new();
    private string? _pendingAnchor;

    private sealed class ListState
    {
        public bool Ordered { get; init; }
        public int Counter { get; set; }
    }

    public PageBuilder(HtmlDocument document, Uri pageUri, string? externalCss = null)
    {
        _document = document;
        _pageUri = pageUri;
        _baseUri = ResolveBase(document, pageUri);
        _resolver = new StyleResolver(CollectStyles(document, externalCss));
    }

    public PageModel Build(PageRenderInfo info)
    {
        ExtractMetadata();

        _page.Url = info.FinalUrl;
        _page.ContentType = info.ContentType;
        _page.Bytes = info.Bytes;
        _page.ElapsedMs = info.ElapsedMs;
        _page.StatusCode = info.StatusCode;
        _page.Charset = info.Charset ?? _document.Charset;
        _page.Internal = info.Internal;

        var rootStyle = new ComputedStyle { Color = "#20242e" };
        Walk(_document.Root, rootStyle);

        FlushRun();

        TableFormatter.Align(_blocks);
        var filtered = ContentFilter.Apply(_blocks, _pageUri);
        if (filtered > 0)
        {
            _page.Meta.Add(new MetaEntry { Name = "filtered", Value = filtered.ToString() });
        }

        if (_blocks.Count == 0)
        {
            _blocks.Add(new BlockModel
            {
                Kind = "notice",
                Spans =
                {
                    new SpanModel
                    {
                        Text = "На этой странице нет текста, который Sirius может показать. " +
                               "Возможно, для неё требуется JavaScript.",
                        Italic = true,
                        Color = "#6b7280"
                    }
                }
            });
        }

        _page.Blocks = _blocks;
        _page.Outline = _outline;
        _page.Links = _links;
        _page.WordCount = CountWords();

        if (string.IsNullOrWhiteSpace(_page.Title))
        {
            _page.Title = _pageUri.Host.Length > 0 ? _pageUri.Host : _page.Url;
        }

        return _page;
    }

    private void Walk(HtmlElement element, ComputedStyle parentStyle)
    {
        var tag = element.Tag;

        if (SkipTags.Contains(tag)) return;

        var style = _resolver.Resolve(element, parentStyle);
        if (style.DisplayNone) return;

        if (HeadingTags.Contains(tag))
        {
            var level = tag[1] - '0';
            var text = Collapse(element.InnerText());
            if (text.Length > 2)
            {
                _outline.Add(new OutlineEntry
                {
                    Level = level,
                    Text = text,
                    Anchor = element.Id.Length > 0 ? "#" + element.Id : null
                });
            }

            BeginBlock("heading", style, level, anchor: element.Id);
            _headingLevel = level;
            WalkChildren(element, style);
            _headingLevel = 0;
            FlushRun();
            return;
        }

        switch (tag)
        {
            case "br":
                ForceBreak();
                return;

            case "hr":
                FlushRun();
                _blocks.Add(new BlockModel { Kind = "hr", SpaceBefore = 10, SpaceAfter = 10 });
                _pendingAnchor = null;
                return;

            case "img":
                AddImage(element, style);
                return;

            case "ul":
            case "ol":
            case "menu":
            {
                PushList(element, tag == "ol");
                WalkChildren(element, style);
                PopList();
                FlushRun();
                return;
            }

            case "li":
            {
                var list = _lists.Count > 0 ? _lists[^1] : null;
                var marker = list is { Ordered: true } ? $"{list.Counter++}." : "•";
                BeginBlock("list_item", style, marker: marker, anchor: element.Id);
                WalkChildren(element, style);
                FlushRun();
                return;
            }

            case "blockquote":
            {
                _quoteDepth++;
                FlushRun();
                WalkChildren(element, style);
                FlushRun();
                _quoteDepth = Math.Max(0, _quoteDepth - 1);
                return;
            }

            case "pre":
            case "xmp":
            case "listing":
            case "plaintext":
            {
                var preStyle = style.Clone();
                preStyle.Preformatted = true;
                BeginBlock("pre", preStyle, anchor: element.Id);
                WalkChildren(element, preStyle);
                FlushRun();
                return;
            }

            case "table":
                FlushRun();
                WalkChildren(element, style);
                FlushRun();
                return;

            case "tr":
            {
                BeginBlock("table_row", style);
                _rowCells.Push(0);
                WalkChildren(element, style);
                _rowCells.Pop();
                FlushRun();
                return;
            }

            case "td":
            case "th":
            {
                if (_rowCells.Count > 0 && _rowCells.Peek() > 0) AddText("  │  ", style, false);
                if (_rowCells.Count > 0) _rowCells.Push(_rowCells.Pop() + 1);

                var cellStyle = style.Clone();
                if (tag == "th") cellStyle.Bold = true;
                WalkChildren(element, cellStyle);
                return;
            }

            case "a":
                WalkLink(element, style);
                return;

            case "strong":
            case "b":
                WalkWith(element, style, s => s.Bold = true);
                return;

            case "em":
            case "i":
            case "cite":
            case "dfn":
            case "var":
                WalkWith(element, style, s => s.Italic = true);
                return;

            case "code":
            case "kbd":
            case "samp":
            case "tt":
                WalkWith(element, style, s => s.Background = "#eef1f6");
                return;

            case "u":
            case "ins":
                WalkWith(element, style, s => s.Underline = true);
                return;

            case "s":
            case "strike":
            case "del":
                WalkWith(element, style, s => s.Strike = true);
                return;

            case "mark":
                WalkWith(element, style, s => s.Background = "#ffe58a");
                return;

            case "font":
                WalkWith(element, style, s =>
                {
                    var color = CssColors.Normalize(element.GetAttribute("color"));
                    if (color is not null) s.Color = color;
                });
                return;

            case "input":
            case "button":
                return;
        }

        if (BlockTags.Contains(tag))
        {
            var kind = tag is "html" or "body" ? "container" : "paragraph";
            BeginBlock(kind, style, anchor: element.Id);
            WalkChildren(element, style);
            if (kind != "container") FlushRun();
            else FlushRun();
            return;
        }

        // Unknown/custom elements: inline by default.
        WalkChildren(element, style);
    }

    private void WalkChildren(HtmlElement element, ComputedStyle style)
    {
        foreach (var child in element.Children)
        {
            if (child is HtmlText text)
            {
                AddTextNode(text.Text, style);
            }
            else if (child is HtmlElement childElement)
            {
                Walk(childElement, style);
            }
        }
    }

    private void WalkWith(HtmlElement element, ComputedStyle style, Action<ComputedStyle> modify)
    {
        var derived = style.Clone();
        modify(derived);
        WalkChildren(element, derived);
    }

    private void WalkLink(HtmlElement element, ComputedStyle style)
    {
        var href = UrlNormalizer.ResolveHref(element.GetAttribute("href"), _baseUri);
        if (href is null)
        {
            WalkChildren(element, style);
            return;
        }

        href = LinkSanitizer.Sanitize(UrlNormalizer.UnwrapRedirect(href));
        if (href is null)
        {
            WalkChildren(element, style);
            return;
        }

        var linkStyle = style.Clone();
        if (linkStyle.Color is null) linkStyle.Color = "#1a5ce5";
        linkStyle.Underline = true;

        var text = Collapse(element.InnerText());
        if (text.Length > 0 && text.Length < 300 && _links.Count < 500)
        {
            var key = href + "\u0001" + text;
            if (_linkKeys.Add(key))
            {
                _links.Add(new LinkEntry { Text = text, Url = href });
            }
        }

        var previous = _link;
        _link = href;
        WalkChildren(element, linkStyle);
        _link = previous;
    }

    private void AddImage(HtmlElement element, ComputedStyle style)
    {
        var alt = (element.GetAttribute("alt") ?? element.GetAttribute("title") ?? string.Empty).Trim();
        if (alt.Length == 0) return;

        var placeholder = new ComputedStyle
        {
            Color = "#6b7280",
            Italic = true,
            Background = "#f1f3f7"
        };

        AddText($"[ {alt} ]", placeholder, false);
    }

    private void AddTextNode(string raw, ComputedStyle style)
    {
        if (string.IsNullOrEmpty(raw)) return;

        if (style.Preformatted)
        {
            AddText(NormalizeNewlines(raw), style, false, preserve: true);
            return;
        }

        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsWhiteSpace(c) || c == '\u00A0')
            {
                if (!_lastWasSpace)
                {
                    sb.Append(' ');
                    _lastWasSpace = true;
                }
            }
            else if (!char.IsControl(c) || c == '\t')
            {
                sb.Append(c);
                _lastWasSpace = false;
            }
        }

        if (sb.Length > 0) AddText(sb.ToString(), style, false);
    }

    private void AddText(string text, ComputedStyle style, bool code, bool preserve = false)
    {
        if (string.IsNullOrEmpty(text)) return;

        EnsureRun(style);

        var span = new SpanModel
        {
            Text = text,
            Bold = style.Bold || _headingLevel > 0,
            Italic = style.Italic,
            Code = code,
            Underline = style.Underline,
            Strike = style.Strike,
            Link = _link,
            Color = style.Color,
            Background = style.Background
        };

        if (_run!.Spans.Count > 0 && !preserve)
        {
            var last = _run.Spans[^1];
            if (SameFormatting(last, span))
            {
                last.Text += span.Text;
                return;
            }
        }

        _run.Spans.Add(span);
    }

    private void ForceBreak()
    {
        if (_run is null) EnsureRun(null);
        _run!.Spans.Add(new SpanModel { Text = "\n" });
        _lastWasSpace = true;
    }

    private static bool SameFormatting(SpanModel a, SpanModel b) =>
        a.Bold == b.Bold && a.Italic == b.Italic && a.Code == b.Code &&
        a.Underline == b.Underline && a.Strike == b.Strike && a.Mark == b.Mark &&
        a.Color == b.Color && a.Background == b.Background &&
        string.Equals(a.Link, b.Link, StringComparison.Ordinal);

    private void EnsureRun(ComputedStyle? style, string kind = "paragraph", int level = 0, string? marker = null)
    {
        if (_run is not null) return;

        if (kind == "paragraph" && _quoteDepth > 0) kind = "quote";

        _run = new BlockModel
        {
            Kind = kind,
            Level = level,
            Marker = marker,
            Indent = CurrentIndent(),
            Anchor = _pendingAnchor,
            SpaceBefore = kind switch
            {
                "heading" => level == 1 ? 8 : 18,
                "pre" => 8,
                "list_item" => 2,
                _ => 0
            },
            SpaceAfter = kind switch
            {
                "heading" => 8,
                "pre" => 12,
                "list_item" => 4,
                _ => 10
            }
        };

        if (style is not null) _run.Align = style.Align;
        _pendingAnchor = null;
        _lastWasSpace = true;
    }

    private void BeginBlock(string kind, ComputedStyle style, int level = 0, string? marker = null, string? anchor = null)
    {
        FlushRun();
        if (kind == "paragraph" && _quoteDepth > 0) kind = "quote";
        _pendingAnchor = string.IsNullOrEmpty(anchor) ? null : "#" + anchor;
        _run = new BlockModel
        {
            Kind = kind,
            Level = level,
            Marker = marker,
            Indent = CurrentIndent(),
            Align = style.Align,
            Anchor = _pendingAnchor,
            SpaceBefore = kind switch
            {
                "heading" => level switch { 1 => 6, 2 => 22, _ => 16 },
                "pre" => 8,
                "list_item" => 2,
                "table_row" => 2,
                _ => 0
            },
            SpaceAfter = kind switch
            {
                "heading" => level switch { 1 => 14, 2 => 10, _ => 8 },
                "pre" => 12,
                "list_item" => 4,
                "table_row" => 2,
                _ => 10
            }
        };

        _pendingAnchor = null;
        _lastWasSpace = true;
    }

    private void FlushRun()
    {
        if (_run is null) return;

        var block = _run;
        _run = null;
        _lastWasSpace = true;

        TrimTrailingWhitespace(block);
        if (block.Spans.Count == 0) return;
        if (block.Spans.All(s => s.Text.Length == 0)) return;

        _blocks.Add(block);
    }

    private static void TrimTrailingWhitespace(BlockModel block)
    {
        while (block.Spans.Count > 0)
        {
            var last = block.Spans[^1];
            if (last.Text.Contains('\n')) return;

            var trimmed = last.Text.TrimEnd();
            if (trimmed.Length == last.Text.Length) return;

            last.Text = trimmed;
            if (last.Text.Length == 0) block.Spans.RemoveAt(block.Spans.Count - 1);
            else return;
        }
    }

    private int CurrentIndent()
    {
        var indent = _quoteDepth + _lists.Count;
        return Math.Clamp(indent, 0, 6);
    }

    private void PushList(HtmlElement element, bool ordered)
    {
        var start = 1;
        if (ordered && int.TryParse(element.GetAttribute("start"), out var parsedStart) && parsedStart != 0)
        {
            start = parsedStart;
        }

        _lists.Add(new ListState { Ordered = ordered, Counter = start });
    }

    private void PopList()
    {
        if (_lists.Count > 0) _lists.RemoveAt(_lists.Count - 1);
    }

    private void ExtractMetadata()
    {
        string? title = null;
        string? description = null;
        string? siteName = null;
        string? favicon = null;
        string? language = null;
        string? ogTitle = null;

        foreach (var element in _document.Root.Descendants())
        {
            switch (element.Tag)
            {
                case "html":
                    language ??= element.GetAttribute("lang");
                    break;

                case "title":
                    title ??= Collapse(element.InnerText());
                    break;

                case "meta":
                {
                    var name = element.GetAttribute("name")?.ToLowerInvariant();
                    var property = element.GetAttribute("property")?.ToLowerInvariant();
                    var content = element.GetAttribute("content");
                    if (string.IsNullOrWhiteSpace(content)) break;

                    switch (name ?? property)
                    {
                        case "description":
                            description ??= Collapse(content);
                            break;
                        case "og:description" when description is null:
                            description = Collapse(content);
                            break;
                        case "og:title":
                            ogTitle ??= Collapse(content);
                            break;
                        case "og:site_name":
                            siteName ??= Collapse(content);
                            break;
                    }
                    break;
                }

                case "link":
                {
                    var rel = element.GetAttribute("rel")?.ToLowerInvariant() ?? string.Empty;
                    if (!rel.Contains("icon")) break;
                    favicon ??= UrlNormalizer.ResolveHref(element.GetAttribute("href"), _baseUri);
                    break;
                }
            }
        }

        _page.Title = !string.IsNullOrWhiteSpace(title) ? title! : ogTitle ?? string.Empty;
        _page.Description = description;
        _page.SiteName = siteName;
        _page.Favicon = favicon;
        _page.Language = language;

        foreach (var meta in new[] { ("description", description), ("site", siteName), ("language", language) })
        {
            if (meta.Item2 is not null && _seenMeta.Add(meta.Item1))
            {
                _page.Meta.Add(new MetaEntry { Name = meta.Item1, Value = meta.Item2 });
            }
        }
    }

    private int CountWords()
    {
        var count = 0;
        foreach (var block in _blocks)
        {
            foreach (var span in block.Spans)
            {
                if (span.Link is not null && span.Text.StartsWith('[') && span.Text.EndsWith(']')) continue;
                foreach (var word in span.Text.Split(
                             new[] { ' ', '\t', '\n', '\r', '\u00A0' },
                             StringSplitOptions.RemoveEmptyEntries))
                {
                    if (word.Any(char.IsLetterOrDigit)) count++;
                }
            }
        }
        return count;
    }

    private static List<CssRule> CollectStyles(HtmlDocument document, string? externalCss)
    {
        var rules = new List<CssRule>();
        foreach (var element in document.Root.Descendants())
        {
            if (element.Tag != "style") continue;
            var css = element.InnerText();
            if (string.IsNullOrWhiteSpace(css)) continue;
            rules.AddRange(CssParser.ParseStylesheet(css));
        }

        if (!string.IsNullOrWhiteSpace(externalCss))
        {
            rules.AddRange(CssParser.ParseStylesheet(externalCss));
        }

        // A tiny built-in reset that keeps pages readable in a text-first browser.
        rules.AddRange(CssParser.ParseStylesheet(
            "nav,footer,form,script,style,noscript{display:none}" +
            "a{color:#1a5ce5;text-decoration:underline}" +
            "pre,code{white-space:pre}" +
            ".result__extras__url span{display:none}" +
            ".feedback{display:none}"));
        return rules;
    }

    private static Uri? ResolveBase(HtmlDocument document, Uri pageUri)
    {
        if (string.IsNullOrWhiteSpace(document.BaseHref)) return pageUri;
        return Uri.TryCreate(pageUri, document.BaseHref, out var resolved) ? resolved : pageUri;
    }

    private static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string Collapse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}

public sealed class PageRenderInfo
{
    public string FinalUrl { get; init; } = string.Empty;
    public string ContentType { get; init; } = "text/html";
    public string? Charset { get; init; }
    public long Bytes { get; init; }
    public int ElapsedMs { get; init; }
    public int StatusCode { get; init; }
    public bool Internal { get; init; }
}
