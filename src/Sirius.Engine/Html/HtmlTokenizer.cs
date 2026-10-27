using System.Text;

namespace Sirius.Engine.Html;

/// <summary>
/// Tolerant HTML tokenizer. It is not a full WHATWG state machine, but it handles
/// real-world markup: comments, doctypes, quoted attributes, raw text elements and
/// character references inside attribute values.
/// </summary>
public static class HtmlTokenizer
{
    private static readonly HashSet<string> RawTextTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "xmp", "iframe", "noembed", "noframes", "plaintext"
    };

    private static readonly HashSet<string> RcdataTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "title", "textarea"
    };

    public static IEnumerable<HtmlToken> Tokenize(string html)
    {
        if (string.IsNullOrEmpty(html)) yield break;

        var n = html.Length;
        var text = new StringBuilder();
        var i = 0;

        while (i < n)
        {
            var c = html[i];
            if (c != '<')
            {
                text.Append(c);
                i++;
                continue;
            }

            // Comment.
            if (StartsWith(html, i, "<!--"))
            {
                FlushText(text, out var textToken);
                if (textToken != null) yield return textToken;

                var end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                var comment = end < 0 ? html[(i + 4)..] : html[(i + 4)..end];
                yield return new HtmlToken { Kind = HtmlTokenKind.Comment, Text = comment };
                i = end < 0 ? n : end + 3;
                continue;
            }

            // Doctype or bogus comment.
            if (StartsWith(html, i, "<!") || StartsWith(html, i, "<?"))
            {
                FlushText(text, out var textToken);
                if (textToken != null) yield return textToken;

                var end = html.IndexOf('>', i + 2);
                var body = end < 0 ? html[(i + 2)..] : html[(i + 2)..end];
                yield return new HtmlToken { Kind = HtmlTokenKind.Doctype, Text = body.Trim() };
                i = end < 0 ? n : end + 1;
                continue;
            }

            var isEnd = i + 1 < n && html[i + 1] == '/';
            var nameStart = isEnd ? i + 2 : i + 1;
            if (nameStart >= n || !IsNameStart(html[nameStart]))
            {
                text.Append('<');
                i++;
                continue;
            }

            FlushText(text, out var leadingText);
            if (leadingText != null) yield return leadingText;

            i = nameStart;
            var name = ReadName(html, ref i);
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var selfClosing = false;
            var closed = false;

            while (i < n)
            {
                SkipWhitespace(html, ref i);
                if (i >= n) break;

                if (html[i] == '>')
                {
                    i++;
                    closed = true;
                    break;
                }

                if (html[i] == '/' && i + 1 < n && html[i + 1] == '>')
                {
                    selfClosing = true;
                    i += 2;
                    closed = true;
                    break;
                }

                if (html[i] == '/')
                {
                    i++;
                    continue;
                }

                var attrName = ReadAttributeName(html, ref i);
                if (attrName.Length == 0)
                {
                    i++;
                    continue;
                }

                SkipWhitespace(html, ref i);
                var attrValue = string.Empty;
                if (i < n && html[i] == '=')
                {
                    i++;
                    SkipWhitespace(html, ref i);
                    attrValue = ReadAttributeValue(html, ref i);
                }

                attributes.TryAdd(attrName, EntityDecoder.Decode(attrValue));
            }

            if (!closed && i >= n) yield break;

            if (isEnd)
            {
                yield return new HtmlToken { Kind = HtmlTokenKind.EndTag, Name = name };
                continue;
            }

            yield return new HtmlToken
            {
                Kind = HtmlTokenKind.StartTag,
                Name = name,
                Attributes = attributes,
                SelfClosing = selfClosing
            };

            if (selfClosing) continue;

            // Raw text / RCDATA elements: consume everything up to the matching end tag.
            if (RawTextTags.Contains(name) || RcdataTags.Contains(name))
            {
                var closeIndex = FindEndTag(html, i, name);
                var raw = closeIndex < 0 ? html[i..] : html[i..closeIndex];
                if (RcdataTags.Contains(name)) raw = EntityDecoder.Decode(raw);

                yield return new HtmlToken
                {
                    Kind = HtmlTokenKind.RawText,
                    Name = name,
                    Text = raw
                };

                if (closeIndex < 0)
                {
                    i = n;
                    yield break;
                }

                var closeEnd = html.IndexOf('>', closeIndex);
                i = closeEnd < 0 ? n : closeEnd + 1;
                yield return new HtmlToken { Kind = HtmlTokenKind.EndTag, Name = name };
            }
        }

        FlushText(text, out var trailing);
        if (trailing != null) yield return trailing;
    }

    private static void FlushText(StringBuilder text, out HtmlToken? token)
    {
        token = null;
        if (text.Length == 0) return;
        token = new HtmlToken { Kind = HtmlTokenKind.Text, Text = text.ToString() };
        text.Clear();
    }

    private static int FindEndTag(string html, int from, string name)
    {
        var needle = "</" + name;
        var index = from;
        while (index < html.Length)
        {
            var found = html.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0) return -1;
            var after = found + needle.Length;
            if (after >= html.Length) return found;
            var ch = html[after];
            if (ch is '>' or '/' || char.IsWhiteSpace(ch)) return found;
            index = after;
        }
        return -1;
    }

    private static bool StartsWith(string haystack, int index, string value) =>
        index + value.Length <= haystack.Length &&
        string.CompareOrdinal(haystack, index, value, 0, value.Length) == 0;

    private static bool IsNameStart(char c) => char.IsLetter(c);

    private static bool IsNameChar(char c) =>
        char.IsLetterOrDigit(c) || c is '-' or '_' or ':' or '.';

    private static string ReadName(string html, ref int i)
    {
        var start = i;
        while (i < html.Length && IsNameChar(html[i])) i++;
        return html[start..i].ToLowerInvariant();
    }

    private static void SkipWhitespace(string html, ref int i)
    {
        while (i < html.Length && char.IsWhiteSpace(html[i])) i++;
    }

    private static string ReadAttributeName(string html, ref int i)
    {
        var start = i;
        while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] is not ('=' or '>' or '/' or '<'))
        {
            i++;
        }
        return html[start..i].ToLowerInvariant();
    }

    private static string ReadAttributeValue(string html, ref int i)
    {
        if (i >= html.Length) return string.Empty;

        var quote = html[i];
        if (quote is '"' or '\'')
        {
            i++;
            var start = i;
            while (i < html.Length && html[i] != quote) i++;
            var value = html[start..Math.Min(i, html.Length)];
            if (i < html.Length) i++;
            return value;
        }

        var plainStart = i;
        while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] != '>') i++;
        return html[plainStart..i];
    }
}
