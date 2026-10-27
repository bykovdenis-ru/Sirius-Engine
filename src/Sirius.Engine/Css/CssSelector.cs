using System.Text;
using Sirius.Engine.Html;

namespace Sirius.Engine.Css;

public sealed class AttributeTest
{
    public string Name { get; init; } = string.Empty;
    public string? Operator { get; init; }
    public string? Value { get; init; }
    public bool IgnoreCase { get; init; }

    public bool Matches(HtmlElement element)
    {
        var actual = element.GetAttribute(Name);
        if (actual is null) return false;
        if (Operator is null) return true;

        var expected = Value ?? string.Empty;
        var comparison = IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        return Operator switch
        {
            "=" => actual.Equals(expected, comparison),
            "~=" => actual.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(v => v.Equals(expected, comparison)),
            "|=" => actual.Equals(expected, comparison) ||
                    actual.StartsWith(expected + "-", comparison),
            "^=" => actual.StartsWith(expected, comparison),
            "$=" => actual.EndsWith(expected, comparison),
            "*=" => actual.Contains(expected, comparison),
            _ => false
        };
    }
}

public sealed class SelectorPart
{
    /// <summary>Combinator that connects this part to the previous one: "", " ", ">", "+", "~".</summary>
    public string Combinator { get; init; } = string.Empty;
    public string? Tag { get; set; }
    public string? Id { get; set; }
    public List<string> Classes { get; } = new();
    public List<AttributeTest> Attributes { get; } = new();
    public List<string> Pseudos { get; } = new();
    public bool HasPseudoElement { get; set; }

    public int Specificity =>
        (Id is null ? 0 : 100) +
        (Classes.Count + Attributes.Count + Pseudos.Count) * 10 +
        (Tag is null ? 0 : 1);

    public bool Matches(HtmlElement element)
    {
        if (HasPseudoElement) return false;
        if (Tag is not null && !string.Equals(element.Tag, Tag, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Id is not null && !string.Equals(element.Id, Id, StringComparison.Ordinal)) return false;

        foreach (var className in Classes)
        {
            if (!element.HasClass(className)) return false;
        }

        foreach (var attribute in Attributes)
        {
            if (!attribute.Matches(element)) return false;
        }

        foreach (var pseudo in Pseudos)
        {
            if (!MatchesPseudo(element, pseudo)) return false;
        }

        return true;
    }

    private static bool MatchesPseudo(HtmlElement element, string pseudo)
    {
        switch (pseudo)
        {
            case "first-child":
                return PreviousElementSibling(element) is null;
            case "last-child":
                return NextElementSibling(element) is null;
            case "only-child":
                return PreviousElementSibling(element) is null && NextElementSibling(element) is null;
            case "first-of-type":
                return Siblings(element).TakeWhile(e => e != element)
                    .All(e => !string.Equals(e.Tag, element.Tag, StringComparison.OrdinalIgnoreCase));
            case "last-of-type":
                return Siblings(element).SkipWhile(e => e != element).Skip(1)
                    .All(e => !string.Equals(e.Tag, element.Tag, StringComparison.OrdinalIgnoreCase));
            case "root":
                return string.Equals(element.Tag, "html", StringComparison.OrdinalIgnoreCase);
            case "empty":
                return element.Children.Count == 0 ||
                       element.Children.All(c => c is HtmlText text && text.Text.Length == 0);
            case "hover" or "active" or "focus" or "focus-within" or "visited" or "target":
                return false;
        }

        if (pseudo.StartsWith("nth-child(", StringComparison.Ordinal) && pseudo.EndsWith(')'))
        {
            return MatchesNth(element, pseudo[10..^1]);
        }

        if (pseudo.StartsWith("nth-of-type(", StringComparison.Ordinal) && pseudo.EndsWith(')'))
        {
            var siblings = Siblings(element)
                .Where(e => string.Equals(e.Tag, element.Tag, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var index = siblings.IndexOf(element) + 1;
            return MatchesNthExpression(index, pseudo[12..^1]);
        }

        if (pseudo.StartsWith("not(", StringComparison.Ordinal) && pseudo.EndsWith(')'))
        {
            var inner = pseudo[4..^1];
            var parsed = CssSelector.Parse(inner);
            return parsed is null || !parsed.Matches(element);
        }

        return false;
    }

    private static bool MatchesNth(HtmlElement element, string expression)
    {
        var siblings = Siblings(element).ToList();
        var index = siblings.IndexOf(element) + 1;
        return index > 0 && MatchesNthExpression(index, expression);
    }

    private static bool MatchesNthExpression(int index, string expression)
    {
        expression = expression.Replace(" ", string.Empty).ToLowerInvariant();
        if (expression == "odd") return index % 2 == 1;
        if (expression == "even") return index % 2 == 0;
        if (int.TryParse(expression, out var exact)) return index == exact;

        var nIndex = expression.IndexOf('n');
        if (nIndex < 0) return false;

        var aText = expression[..nIndex];
        var bText = expression[(nIndex + 1)..];
        var a = aText switch { "" or "+" => 1, "-" => -1, _ => int.TryParse(aText, out var parsed) ? parsed : 0 };
        var b = bText.Length == 0 ? 0 : int.TryParse(bText, out var parsedB) ? parsedB : 0;

        if (a == 0) return index == b;
        var delta = index - b;
        return delta % a == 0 && delta / a >= 0;
    }

    private static List<HtmlElement> Siblings(HtmlElement element) =>
        element.Parent?.Children.OfType<HtmlElement>().ToList() ?? new List<HtmlElement>();

    private static HtmlElement? PreviousElementSibling(HtmlElement element)
    {
        if (element.Parent is null) return null;
        HtmlElement? previous = null;
        foreach (var child in element.Parent.Children)
        {
            if (child == element) return previous;
            if (child is HtmlElement candidate) previous = candidate;
        }
        return null;
    }

    private static HtmlElement? NextElementSibling(HtmlElement element)
    {
        if (element.Parent is null) return null;
        var seen = false;
        foreach (var child in element.Parent.Children)
        {
            if (child == element)
            {
                seen = true;
                continue;
            }
            if (seen && child is HtmlElement candidate) return candidate;
        }
        return null;
    }
}

public sealed class CssSelector
{
    public List<SelectorPart> Parts { get; } = new();
    public int Specificity => Parts.Sum(p => p.Specificity);

    public bool Matches(HtmlElement element)
    {
        if (Parts.Count == 0) return false;
        return MatchesAt(element, Parts.Count - 1);
    }

    private bool MatchesAt(HtmlElement element, int index)
    {
        var part = Parts[index];
        if (!part.Matches(element)) return false;
        if (index == 0) return true;

        switch (part.Combinator)
        {
            case ">":
                return element.Parent is HtmlElement parent && MatchesAt(parent, index - 1);

            case " ":
            {
                var ancestor = element.Parent;
                while (ancestor is not null)
                {
                    if (MatchesAt(ancestor, index - 1)) return true;
                    ancestor = ancestor.Parent;
                }
                return false;
            }

            case "+":
            {
                var previous = PreviousSibling(element);
                return previous is not null && MatchesAt(previous, index - 1);
            }

            case "~":
            {
                var current = PreviousSibling(element);
                while (current is not null)
                {
                    if (MatchesAt(current, index - 1)) return true;
                    current = PreviousSibling(current);
                }
                return false;
            }

            default:
                return false;
        }
    }

    private static HtmlElement? PreviousSibling(HtmlElement element)
    {
        if (element.Parent is null) return null;
        HtmlElement? previous = null;
        foreach (var child in element.Parent.Children)
        {
            if (child == element) return previous;
            if (child is HtmlElement candidate) previous = candidate;
        }
        return null;
    }

    public static List<CssSelector> ParseList(string selectorText)
    {
        var result = new List<CssSelector>();
        foreach (var selector in SplitTopLevel(selectorText, ','))
        {
            var parsed = Parse(selector);
            if (parsed is not null && parsed.Parts.Count > 0) result.Add(parsed);
        }
        return result;
    }

    public static CssSelector? Parse(string selectorText)
    {
        var text = selectorText.Trim();
        if (text.Length == 0) return null;

        var selector = new CssSelector();
        var i = 0;
        var combinator = string.Empty;
        var pendingDescendant = false;

        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                pendingDescendant = true;
                i++;
            }

            if (i >= text.Length) break;

            if (text[i] is '>' or '+' or '~')
            {
                combinator = text[i].ToString();
                pendingDescendant = false;
                i++;
                continue;
            }

            var start = i;
            var depth = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (c is '[' or '(') depth++;
                else if (c is ']' or ')') depth = Math.Max(0, depth - 1);
                else if (depth == 0 && (char.IsWhiteSpace(c) || c is '>' or '+' or '~' or ',')) break;
                i++;
            }

            var compound = text[start..i];
            if (compound.Length > 0)
            {
                var part = ParseCompound(compound);
                var previous = selector.Parts.Count == 0
                    ? string.Empty
                    : combinator.Length > 0
                        ? combinator
                        : pendingDescendant
                            ? " "
                            : " ";

                var finalPart = new SelectorPart
                {
                    Combinator = previous,
                    Tag = part.Tag,
                    Id = part.Id,
                    HasPseudoElement = part.HasPseudoElement
                };
                finalPart.Classes.AddRange(part.Classes);
                finalPart.Attributes.AddRange(part.Attributes);
                finalPart.Pseudos.AddRange(part.Pseudos);
                selector.Parts.Add(finalPart);

                combinator = string.Empty;
                pendingDescendant = false;
            }
            else
            {
                i++;
            }
        }

        return selector.Parts.Count == 0 ? null : selector;
    }

    private static SelectorPart ParseCompound(string text)
    {
        var part = new SelectorPart();
        var i = 0;

        if (i < text.Length && text[i] == '*')
        {
            i++;
        }
        else if (i < text.Length && IsIdentifierStart(text[i]))
        {
            var start = i;
            while (i < text.Length && IsIdentifierChar(text[i])) i++;
            var tag = text[start..i];
            var pipe = tag.LastIndexOf('|');
            if (pipe >= 0) tag = tag[(pipe + 1)..];
            if (tag.Length > 0 && tag != "*") part.Tag = tag.ToLowerInvariant();
        }

        while (i < text.Length)
        {
            var c = text[i];
            if (c == '.')
            {
                i++;
                part.Classes.Add(ReadIdentifier(text, ref i));
            }
            else if (c == '#')
            {
                i++;
                part.Id = ReadIdentifier(text, ref i);
            }
            else if (c == '[')
            {
                var end = FindClosing(text, i, '[', ']');
                if (end < 0) break;
                var test = ParseAttributeTest(text[(i + 1)..end]);
                if (test is not null) part.Attributes.Add(test);
                i = end + 1;
            }
            else if (c == ':')
            {
                i++;
                if (i < text.Length && text[i] == ':')
                {
                    part.HasPseudoElement = true;
                    i++;
                }

                var name = ReadIdentifier(text, ref i).ToLowerInvariant();
                if (name.Length == 0)
                {
                    i++;
                    continue;
                }

                if (i < text.Length && text[i] == '(')
                {
                    var end = FindClosing(text, i, '(', ')');
                    if (end < 0) break;
                    name += "(" + text[(i + 1)..end].Trim() + ")";
                    i = end + 1;
                }

                part.Pseudos.Add(name);
            }
            else if (c == '*')
            {
                i++;
            }
            else
            {
                i++;
            }
        }

        return part;
    }

    private static AttributeTest? ParseAttributeTest(string body)
    {
        body = body.Trim();
        if (body.Length == 0) return null;

        var ignoreCase = false;
        if (body.EndsWith(" i", StringComparison.OrdinalIgnoreCase))
        {
            ignoreCase = true;
            body = body[..^2].Trim();
        }

        var operators = new[] { "~=", "|=", "^=", "$=", "*=", "=" };
        foreach (var op in operators)
        {
            var index = body.IndexOf(op, StringComparison.Ordinal);
            if (index <= 0) continue;

            var name = body[..index].Trim().ToLowerInvariant();
            var value = body[(index + op.Length)..].Trim().Trim('"', '\'');
            return new AttributeTest { Name = name, Operator = op, Value = value, IgnoreCase = ignoreCase };
        }

        return new AttributeTest { Name = body.ToLowerInvariant() };
    }

    private static int FindClosing(string text, int start, char open, char close)
    {
        var depth = 0;
        var quote = '\0';
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                continue;
            }

            if (c is '"' or '\'') quote = c;
            else if (c == open) depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c is '_' or '-' || c > 127;

    private static bool IsIdentifierChar(char c) =>
        char.IsLetterOrDigit(c) || c is '-' or '_' || c > 127;

    private static string ReadIdentifier(string text, ref int i)
    {
        var start = i;
        while (i < text.Length && IsIdentifierChar(text[i])) i++;
        return text[start..i];
    }

    private static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        var depth = 0;
        var quote = '\0';
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                continue;
            }

            if (c is '"' or '\'') quote = c;
            else if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth = Math.Max(0, depth - 1);
            else if (c == separator && depth == 0)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }
}
