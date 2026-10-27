using System.Globalization;
using System.Text;

namespace Sirius.Engine.Html;

/// <summary>Minimal but practical HTML character reference decoder.</summary>
public static class EntityDecoder
{
    private static readonly Dictionary<string, string> Named = new(StringComparer.Ordinal)
    {
        ["amp"] = "&", ["lt"] = "<", ["gt"] = ">", ["quot"] = "\"", ["apos"] = "'",
        ["nbsp"] = "\u00A0", ["ensp"] = "\u2002", ["emsp"] = "\u2003", ["thinsp"] = "\u2009",
        ["zwnj"] = "\u200C", ["zwj"] = "\u200D", ["lrm"] = "\u200E", ["rlm"] = "\u200F",
        ["shy"] = "\u00AD", ["copy"] = "©", ["reg"] = "®", ["trade"] = "™",
        ["hellip"] = "…", ["mdash"] = "—", ["ndash"] = "–", ["minus"] = "−",
        ["laquo"] = "«", ["raquo"] = "»", ["lsaquo"] = "‹", ["rsaquo"] = "›",
        ["ldquo"] = "“", ["rdquo"] = "”", ["lsquo"] = "‘", ["rsquo"] = "’",
        ["bdquo"] = "„", ["sbquo"] = "‚", ["dagger"] = "†", ["Dagger"] = "‡",
        ["bull"] = "•", ["middot"] = "·", ["sect"] = "§", ["para"] = "¶",
        ["times"] = "×", ["divide"] = "÷", ["plusmn"] = "±", ["deg"] = "°",
        ["micro"] = "µ", ["euro"] = "€", ["pound"] = "£", ["yen"] = "¥", ["cent"] = "¢",
        ["frac12"] = "½", ["frac14"] = "¼", ["frac34"] = "¾", ["sup1"] = "¹",
        ["sup2"] = "²", ["sup3"] = "³", ["larr"] = "←", ["rarr"] = "→",
        ["uarr"] = "↑", ["darr"] = "↓", ["harr"] = "↔", ["crarr"] = "↵",
        ["lArr"] = "⇐", ["rArr"] = "⇒", ["hArr"] = "⇔", ["forall"] = "∀",
        ["part"] = "∂", ["exist"] = "∃", ["empty"] = "∅", ["nabla"] = "∇",
        ["isin"] = "∈", ["notin"] = "∉", ["ni"] = "∋", ["prod"] = "∏",
        ["sum"] = "∑", ["lowast"] = "∗", ["radic"] = "√", ["prop"] = "∝",
        ["infin"] = "∞", ["ang"] = "∠", ["and"] = "∧", ["or"] = "∨",
        ["cap"] = "∩", ["cup"] = "∪", ["int"] = "∫", ["there4"] = "∴",
        ["sim"] = "∼", ["cong"] = "≅", ["asymp"] = "≈", ["ne"] = "≠",
        ["equiv"] = "≡", ["le"] = "≤", ["ge"] = "≥", ["sub"] = "⊂", ["sup"] = "⊃",
        ["nsub"] = "⊄", ["sube"] = "⊆", ["supe"] = "⊇", ["oplus"] = "⊕",
        ["otimes"] = "⊗", ["perp"] = "⊥", ["sdot"] = "⋅", ["lceil"] = "⌈",
        ["rceil"] = "⌉", ["lfloor"] = "⌊", ["rfloor"] = "⌋", ["lang"] = "〈",
        ["rang"] = "〉", ["loz"] = "◊", ["spades"] = "♠", ["clubs"] = "♣",
        ["hearts"] = "♥", ["diams"] = "♦", ["star"] = "☆", ["starf"] = "★",
        ["check"] = "✓", ["cross"] = "✗", ["male"] = "♂", ["female"] = "♀",
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ",
        ["epsilon"] = "ε", ["lambda"] = "λ", ["mu"] = "μ", ["pi"] = "π",
        ["sigma"] = "σ", ["omega"] = "ω", ["Omega"] = "Ω", ["Delta"] = "Δ",
        ["Sigma"] = "Σ", ["Pi"] = "Π", ["nbsp;"] = "\u00A0",
        ["auml"] = "ä", ["ouml"] = "ö", ["uuml"] = "ü", ["Auml"] = "Ä",
        ["Ouml"] = "Ö", ["Uuml"] = "Ü", ["szlig"] = "ß", ["eacute"] = "é",
        ["egrave"] = "è", ["agrave"] = "à", ["ccedil"] = "ç", ["aacute"] = "á",
        ["iacute"] = "í", ["oacute"] = "ó", ["uacute"] = "ú", ["ntilde"] = "ñ",
        ["AElig"] = "Æ", ["aelig"] = "æ", ["oslash"] = "ø", ["Oslash"] = "Ø",
        ["aring"] = "å", ["Aring"] = "Å", ["iquest"] = "¿", ["iexcl"] = "¡",
        ["sol"] = "/", ["bsol"] = "\\", ["lowbar"] = "_", ["num"] = "#",
        ["percnt"] = "%", ["commat"] = "@", ["dollar"] = "$", ["excl"] = "!",
        ["quest"] = "?", ["colon"] = ":", ["semi"] = ";", ["comma"] = ",",
        ["period"] = ".", ["plus"] = "+", ["equals"] = "=", ["ast"] = "*",
        ["verbar"] = "|", ["vert"] = "|", ["lpar"] = "(", ["rpar"] = ")",
        ["lbrack"] = "[", ["rbrack"] = "]", ["lbrace"] = "{", ["rbrace"] = "}",
        ["Hat"] = "^", ["grave"] = "`", ["tilde"] = "˜", ["circ"] = "ˆ"
    };

    private static readonly Dictionary<int, char> Windows1252 = new()
    {
        [0x80] = '€', [0x82] = '‚', [0x83] = 'ƒ', [0x84] = '„', [0x85] = '…',
        [0x86] = '†', [0x87] = '‡', [0x88] = 'ˆ', [0x89] = '‰', [0x8A] = 'Š',
        [0x8B] = '‹', [0x8C] = 'Œ', [0x8E] = 'Ž', [0x91] = '‘', [0x92] = '’',
        [0x93] = '“', [0x94] = '”', [0x95] = '•', [0x96] = '–', [0x97] = '—',
        [0x98] = '˜', [0x99] = '™', [0x9A] = 'š', [0x9B] = '›', [0x9C] = 'œ',
        [0x9E] = 'ž', [0x9F] = 'Ÿ'
    };

    private static readonly HashSet<string> WithoutSemicolon = new(StringComparer.Ordinal)
    {
        "amp", "lt", "gt", "quot", "nbsp", "copy", "reg", "apos"
    };

    public static string Decode(string? input)
    {
        if (string.IsNullOrEmpty(input) || !input.Contains('&')) return input ?? string.Empty;

        var sb = new StringBuilder(input.Length);
        var i = 0;
        while (i < input.Length)
        {
            var c = input[i];
            if (c != '&')
            {
                sb.Append(c);
                i++;
                continue;
            }

            var semi = input.IndexOf(';', i + 1);
            var maxEntity = Math.Min(input.Length, i + 34);
            if (semi > i + 1 && semi <= maxEntity)
            {
                var name = input.Substring(i + 1, semi - i - 1);
                if (name.StartsWith('#'))
                {
                    if (TryNumeric(name[1..], out var decoded))
                    {
                        sb.Append(decoded);
                        i = semi + 1;
                        continue;
                    }
                }
                else if (Named.TryGetValue(name, out var value))
                {
                    sb.Append(value);
                    i = semi + 1;
                    continue;
                }
            }

            // Legacy entities are allowed without the trailing semicolon.
            if (char.IsLetter(c) && i + 1 < input.Length && char.IsLetter(input[i + 1]))
            {
                var end = i + 1;
                while (end < input.Length && end - i <= 10 && char.IsLetterOrDigit(input[end])) end++;
                var raw = input[(i + 1)..end];
                if (WithoutSemicolon.Contains(raw) && Named.TryGetValue(raw, out var legacy))
                {
                    sb.Append(legacy);
                    i = end;
                    continue;
                }
            }

            sb.Append('&');
            i++;
        }

        return sb.ToString();
    }

    private static bool TryNumeric(string body, out string result)
    {
        result = string.Empty;
        var hex = body.StartsWith('x') || body.StartsWith('X');
        var digits = hex ? body[1..] : body;
        if (digits.Length == 0) return false;

        var style = hex ? NumberStyles.HexNumber : NumberStyles.Integer;
        if (!int.TryParse(digits, style, CultureInfo.InvariantCulture, out var codePoint)) return false;
        if (codePoint <= 0 || codePoint > 0x10FFFF) return false;
        if (codePoint is >= 0xD800 and <= 0xDFFF) return false;

        if (Windows1252.TryGetValue(codePoint, out var replacement))
        {
            result = replacement.ToString();
            return true;
        }

        result = char.ConvertFromUtf32(codePoint);
        return true;
    }
}
