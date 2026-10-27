using System.Text.RegularExpressions;
using Sirius.Engine.Models;

namespace Sirius.Engine.Layout;

/// <summary>
/// Removes the parts of a web page that make a text rendering noisy: cookie
/// notices, newsletter prompts, share widgets, duplicated menu labels and empty
/// spacer blocks. The filter is conservative on purpose — real paragraphs are
/// never dropped, only short lines that clearly belong to page chrome.
/// </summary>
public static class ContentFilter
{
    private static readonly Regex CookieNotice = new(
        @"\b(cookie|cookies)\b.{0,80}\b(accept|agree|allow|consent|privacy|policy|использ|приня|соглас|политик)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AcceptButton = new(
        @"^(accept all|accept|i agree|agree|got it|allow all|принять|согласен|понятно|разрешить)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Newsletter = new(
        @"\b(newsletter|subscribe|sign up for|подпишись|подпишитесь|подписаться|рассылка)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SocialPrompt = new(
        @"^(share|share this|tweet|like|follow us|поделиться|поделиться статьёй|читайте также|смотрите также)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Advertisement = new(
        @"^(advertisement|advertising|sponsored|реклама|промо)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Copyright = new(
        @"(©|\(c\)|copyright|все права защищены|all rights reserved)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SignInWall = new(
        @"^(sign in|log in|register|войти|вход|регистрация)( to continue)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SkipLink = new(
        @"^((skip|jump) to (the )?(main )?(content|navigation)|перейти к (содержимому|контенту))$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PunctuationOnly = new(
        @"^[\s\p{P}\p{S}\d]+$",
        RegexOptions.Compiled);

    private static readonly Regex MenuSeparator = new(
        @"^[|\-–—•·/\\:;,.]+$",
        RegexOptions.Compiled);

    private static readonly HashSet<string> FilterableKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "paragraph", "notice", "list_item"
    };

    public static int Apply(List<BlockModel> blocks, Uri pageUri)
    {
        if (blocks.Count == 0) return 0;

        var removed = 0;
        var counts = CountShortLines(blocks);

        removed += RemoveConsecutiveDuplicates(blocks);

        for (var i = blocks.Count - 1; i >= 0; i--)
        {
            var block = blocks[i];
            if (!FilterableKinds.Contains(block.Kind)) continue;

            var text = BlockText(block);
            if (text.Length == 0)
            {
                blocks.RemoveAt(i);
                removed++;
                continue;
            }

            if (text.Length > 180) continue;

            if (i <= 2 && IsTopSearchLink(block, text))
            {
                blocks.RemoveAt(i);
                removed++;
                continue;
            }

            if (IsBoilerplate(text, pageUri))
            {
                blocks.RemoveAt(i);
                removed++;
                continue;
            }

            if (text.Length <= 72 && counts.TryGetValue(Normalize(text), out var repeats) && repeats >= 3)
            {
                blocks.RemoveAt(i);
                removed++;
                continue;
            }
        }

        return removed;
    }

    private static bool IsBoilerplate(string text, Uri pageUri)
    {
        if (PunctuationOnly.IsMatch(text)) return true;
        if (MenuSeparator.IsMatch(text)) return true;
        if (AcceptButton.IsMatch(text)) return true;
        if (SocialPrompt.IsMatch(text)) return true;
        if (Advertisement.IsMatch(text)) return true;
        if (SignInWall.IsMatch(text)) return true;
        if (SkipLink.IsMatch(text)) return true;

        if (CookieNotice.IsMatch(text)) return true;

        if (Newsletter.IsMatch(text) && text.Length <= 120) return true;

        if (Copyright.IsMatch(text) && text.Length <= 160) return true;

        // "Powered by ..." and generator footers are never page content.
        if (text.StartsWith("powered by ", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.StartsWith("сделано на ", StringComparison.OrdinalIgnoreCase)) return true;

        // A line that is just the site name or host repeated at the edges.
        if (text.Length <= 40 && pageUri.Host.Length > 0)
        {
            var host = pageUri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? pageUri.Host[4..]
                : pageUri.Host;
            if (text.Equals(host, StringComparison.OrdinalIgnoreCase)) return true;
            if (text.Equals(host.Split('.')[0], StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static bool IsTopSearchLink(BlockModel block, string text)
    {
        if (text is not ("search" or "поиск" or "search...")) return false;
        return block.Spans.Count > 0 && block.Spans.All(s => s.Link is not null);
    }

    private static Dictionary<string, int> CountShortLines(List<BlockModel> blocks)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var block in blocks)
        {
            if (!FilterableKinds.Contains(block.Kind)) continue;
            var text = BlockText(block);
            if (text.Length == 0 || text.Length > 72) continue;

            var key = Normalize(text);
            counts[key] = counts.TryGetValue(key, out var current) ? current + 1 : 1;
        }
        return counts;
    }

    private static int RemoveConsecutiveDuplicates(List<BlockModel> blocks)
    {
        var removed = 0;
        string? previous = null;

        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (!FilterableKinds.Contains(block.Kind))
            {
                previous = null;
                continue;
            }

            var key = Normalize(BlockText(block));
            if (key.Length > 0 && key == previous)
            {
                blocks.RemoveAt(i);
                removed++;
                i--;
                continue;
            }
            previous = key;
        }

        return removed;
    }

    public static string BlockText(BlockModel block)
    {
        if (block.Spans.Count == 0) return string.Empty;

        var builder = new System.Text.StringBuilder();
        foreach (var span in block.Spans)
        {
            if (builder.Length > 0) builder.Append(' ');
            builder.Append(span.Text);
        }

        return Regex.Replace(builder.ToString().Replace('\u00A0', ' '), @"\s+", " ").Trim();
    }

    private static string Normalize(string text) =>
        Regex.Replace(text.ToLowerInvariant(), @"\s+", " ").Trim().TrimEnd('.', '!', '?');
}
