namespace Sirius.Engine.Html;

/// <summary>
/// Builds a tolerant DOM tree from tokenizer output, applying the most important
/// implicit-close rules (p, li, headings, table sections, anchors).
/// </summary>
public static class HtmlParser
{
    private const int MaxNodes = 300_000;
    private const int MaxDepth = 256;

    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta",
        "param", "source", "track", "wbr", "basefont", "bgsound", "frame", "keygen"
    };

    private static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "blockquote", "details", "dialog", "div", "dl",
        "fieldset", "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4",
        "h5", "h6", "header", "hgroup", "hr", "main", "menu", "nav", "ol", "p", "pre",
        "section", "table", "ul", "li", "dt", "dd", "summary"
    };

    private static readonly HashSet<string> Headings = new(StringComparer.OrdinalIgnoreCase)
    {
        "h1", "h2", "h3", "h4", "h5", "h6"
    };

    private static readonly HashSet<string> TableSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "thead", "tbody", "tfoot"
    };

    public static HtmlDocument Parse(string html)
    {
        var state = new ParseState();
        var doc = state.Document;
        var root = doc.Root;

        state.Stack.Push(root);

        foreach (var token in HtmlTokenizer.Tokenize(html))
        {
            if (state.NodeCount >= MaxNodes) break;

            switch (token.Kind)
            {
                case HtmlTokenKind.Text:
                case HtmlTokenKind.RawText:
                    state.AppendText(token.Text);
                    break;

                case HtmlTokenKind.StartTag:
                    state.OpenElement(token.Name, token.Attributes, token.SelfClosing);
                    break;

                case HtmlTokenKind.EndTag:
                    state.CloseElement(token.Name);
                    break;

                case HtmlTokenKind.Doctype:
                    if (token.Text.StartsWith("doctype", StringComparison.OrdinalIgnoreCase)) break;
                    break;
            }
        }

        state.ExtractDocumentHints();
        return doc;
    }

    private sealed class ParseState
    {
        public HtmlDocument Document { get; } = new();
        public Stack<HtmlElement> Stack { get; } = new();
        public int NodeCount { get; set; }
        private int _suppressed;

        private HtmlElement Current => Stack.Peek();

        public void AppendText(string text)
        {
            if (string.IsNullOrEmpty(text) || _suppressed > 0) return;
            if (NodeCount >= MaxNodes) return;

            if (Current.Children.Count > 0 && Current.Children[^1] is HtmlText last)
            {
                last.Text += text;
                return;
            }

            var node = new HtmlText { Text = text, Parent = Current };
            Current.Children.Add(node);
            NodeCount++;
        }

        public void OpenElement(string tag, Dictionary<string, string> attributes, bool selfClosing)
        {
            if (_suppressed > 0)
            {
                if (!selfClosing && !VoidElements.Contains(tag)) _suppressed++;
                return;
            }

            AutoClose(tag);

            if (Stack.Count >= MaxDepth)
            {
                _suppressed = 1;
                if (selfClosing || VoidElements.Contains(tag)) _suppressed = 0;
                return;
            }

            var element = new HtmlElement(tag) { Parent = Current };
            foreach (var pair in attributes) element.Attributes[pair.Key] = pair.Value;

            Current.Children.Add(element);
            NodeCount++;

            if (selfClosing || VoidElements.Contains(tag)) return;
            Stack.Push(element);
        }

        public void CloseElement(string tag)
        {
            if (_suppressed > 0)
            {
                _suppressed--;
                return;
            }

            if (VoidElements.Contains(tag) || tag == "br") return;

            // Find the matching open element; ignore stray end tags.
            if (!Stack.Any(e => e.Tag == tag)) return;

            while (Stack.Count > 1)
            {
                var popped = Stack.Pop();
                if (popped.Tag == tag) return;
            }
        }

        private void AutoClose(string tag)
        {
            if (tag == "li") PopWhile(e => e.Tag == "li");
            else if (tag is "dt" or "dd") PopWhile(e => e.Tag is "dt" or "dd");
            else if (tag is "td" or "th") PopWhile(e => e.Tag is "td" or "th" or "caption");
            else if (tag == "tr") PopWhile(e => e.Tag is "tr" or "td" or "th" or "caption");
            else if (TableSections.Contains(tag))
            {
                PopWhile(e => e.Tag is "thead" or "tbody" or "tfoot" or "tr" or "td" or "th" or "caption");
            }
            else if (tag == "option") PopWhile(e => e.Tag == "option");
            else if (tag == "optgroup") PopWhile(e => e.Tag is "option" or "optgroup");
            else if (tag == "p") PopWhile(e => e.Tag == "p");
            else if (Headings.Contains(tag)) PopWhile(e => Headings.Contains(e.Tag));
            else if (tag == "a") PopWhile(e => e.Tag == "a");
            else if (tag == "button") PopWhile(e => e.Tag == "button");

            if (BlockElements.Contains(tag)) PopWhile(e => e.Tag == "p");

            if (Stack.Count == 1 && tag is "head" or "body")
            {
                // Nothing to do: the document fragment may not contain explicit html/head.
            }
        }

        private void PopWhile(Func<HtmlElement, bool> predicate)
        {
            while (Stack.Count > 1 && predicate(Stack.Peek())) Stack.Pop();
        }

        public void ExtractDocumentHints()
        {
            var doc = Document;

            foreach (var element in doc.Root.Descendants())
            {
                if (element.Tag == "base" && element.GetAttribute("href") is { Length: > 0 } baseHref &&
                    string.IsNullOrEmpty(doc.BaseHref))
                {
                    doc.BaseHref = baseHref.Trim();
                }

                if (element.Tag != "meta") continue;

                var charset = element.GetAttribute("charset");
                if (!string.IsNullOrWhiteSpace(charset) && string.IsNullOrEmpty(doc.Charset))
                {
                    doc.Charset = charset.Trim();
                }

                if (!string.Equals(element.GetAttribute("http-equiv"), "content-type",
                        StringComparison.OrdinalIgnoreCase)) continue;

                var content = element.GetAttribute("content") ?? string.Empty;
                var index = content.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
                if (index >= 0 && string.IsNullOrEmpty(doc.Charset))
                {
                    var value = content[(index + 8)..].Trim().Trim('"', '\'', ';', ' ');
                    if (value.Length > 0) doc.Charset = value;
                }
            }
        }
    }
}
