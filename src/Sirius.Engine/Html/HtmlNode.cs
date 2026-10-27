namespace Sirius.Engine.Html;

public abstract class HtmlNode
{
    public HtmlElement? Parent { get; set; }
    public abstract string NodeName { get; }
}

public sealed class HtmlText : HtmlNode
{
    public string Text { get; set; } = string.Empty;
    public override string NodeName => "#text";
}

public sealed class HtmlElement : HtmlNode
{
    public HtmlElement(string tag)
    {
        Tag = tag;
    }

    public string Tag { get; }
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<HtmlNode> Children { get; } = new();
    public override string NodeName => Tag;

    public string Id => GetAttribute("id") ?? string.Empty;

    public string ClassName => GetAttribute("class") ?? string.Empty;

    public string? GetAttribute(string name) =>
        Attributes.TryGetValue(name, out var value) ? value : null;

    public bool HasAttribute(string name) => Attributes.ContainsKey(name);

    public bool HasClass(string name) =>
        ClassName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<HtmlElement> Descendants()
    {
        var stack = new Stack<HtmlElement>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;
            for (var i = current.Children.Count - 1; i >= 0; i--)
            {
                if (current.Children[i] is HtmlElement element) stack.Push(element);
            }
        }
    }

    public IEnumerable<HtmlElement> Elements() => Descendants().Skip(1);

    public HtmlElement? FindFirst(string tag) =>
        Elements().FirstOrDefault(e => string.Equals(e.Tag, tag, StringComparison.OrdinalIgnoreCase));

    public string InnerText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var node in Children)
        {
            if (node is HtmlText text) sb.Append(text.Text);
            else if (node is HtmlElement element) sb.Append(element.InnerText());
        }
        return sb.ToString();
    }
}

public sealed class HtmlDocument
{
    public HtmlElement Root { get; } = new("#document");
    public string? Charset { get; set; }
    public string? BaseHref { get; set; }

    public HtmlElement? Html => Root.FindFirst("html") ?? Root;
}
