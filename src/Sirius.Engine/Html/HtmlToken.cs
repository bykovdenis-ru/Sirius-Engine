namespace Sirius.Engine.Html;

public enum HtmlTokenKind
{
    Text,
    StartTag,
    EndTag,
    Comment,
    Doctype,
    RawText
}

/// <summary>One token produced by the HTML tokenizer.</summary>
public sealed class HtmlToken
{
    public HtmlTokenKind Kind { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public Dictionary<string, string> Attributes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public bool SelfClosing { get; init; }

    public override string ToString() => Kind switch
    {
        HtmlTokenKind.Text => $"Text({Text.Length})",
        HtmlTokenKind.StartTag => $"<{Name}{(SelfClosing ? " /" : "")}>",
        HtmlTokenKind.EndTag => $"</{Name}>",
        HtmlTokenKind.RawText => $"Raw({Name},{Text.Length})",
        _ => Kind.ToString()
    };
}
