using System.Text;
using Sirius.Engine.Models;

namespace Sirius.Engine.Core;

/// <summary>HTTP fetch result with decoded body and timing information.</summary>
public sealed class FetchResult
{
    public bool Ok { get; set; }
    public int StatusCode { get; set; }
    public string FinalUrl { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public string Charset { get; set; } = "utf-8";
    public string Body { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public int ElapsedMs { get; set; }
    public string? Error { get; set; }
    public string? ErrorKind { get; set; }
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Thin HTTP layer on top of <see cref="HttpClient"/>. Handles redirects, cookies,
/// compression, response size limits and charset detection.
/// </summary>
public sealed class ResourceFetcher : IDisposable
{
    private readonly EngineOptions _options;
    private readonly HttpClient _client;

    static ResourceFetcher()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public ResourceFetcher(EngineOptions options)
    {
        _options = options;

        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            UseCookies = true,
            CookieContainer = new System.Net.CookieContainer()
        };

        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds))
        };

        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,text/plain;q=0.8,image/*;q=0.6,*/*;q=0.5");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", options.AcceptLanguage);
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Cache-Control", "no-cache");
    }

    public async Task<FetchResult> GetAsync(string url, CancellationToken cancellationToken = default)
    {
        var result = new FetchResult { FinalUrl = url };
        var watch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            result.StatusCode = (int)response.StatusCode;
            result.FinalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;

            foreach (var header in response.Headers)
            {
                result.Headers[header.Key] = string.Join(", ", header.Value);
            }
            foreach (var header in response.Content.Headers)
            {
                result.Headers[header.Key] = string.Join(", ", header.Value);
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            result.ContentType = mediaType.ToLowerInvariant();

            var declaredCharset = response.Content.Headers.ContentType?.CharSet;
            var bytes = await ReadLimitedAsync(response.Content, _options.MaxResponseBytes, cancellationToken);
            result.Bytes = bytes.Length;

            result.Charset = DetectCharset(declaredCharset, bytes);
            result.Body = Decode(bytes, result.Charset);
            result.Ok = response.IsSuccessStatusCode;

            if (!result.Ok)
            {
                result.Error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                result.ErrorKind = "http";
            }
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result.Error = $"Превышено время ожидания ({_options.TimeoutSeconds} с).";
            result.ErrorKind = "timeout";
        }
        catch (HttpRequestException ex)
        {
            result.Error = ex.Message;
            result.ErrorKind = "network";
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            result.ErrorKind = "unknown";
        }
        finally
        {
            watch.Stop();
            result.ElapsedMs = (int)watch.ElapsedMilliseconds;
        }

        return result;
    }

    private static async Task<byte[]> ReadLimitedAsync(
        HttpContent content, int limit, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[32 * 1024];
        var total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read <= 0) break;
            total += read;
            if (total > limit)
            {
                buffer.Write(chunk, 0, Math.Max(0, read - (total - limit)));
                break;
            }
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string DetectCharset(string? declared, byte[] bytes)
    {
        if (!string.IsNullOrWhiteSpace(declared))
        {
            var normalized = declared.Trim().Trim('"', '\'').ToLowerInvariant();
            if (normalized.Length > 0) return normalized;
        }

        // Sniff <meta charset> / content-type from the first 4 KB.
        var sniffLength = Math.Min(bytes.Length, 4096);
        var head = Encoding.ASCII.GetString(bytes, 0, sniffLength);
        var index = head.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            var rest = head[(index + 8)..].TrimStart();
            var end = 0;
            while (end < rest.Length && (char.IsLetterOrDigit(rest[end]) || rest[end] is '-' or '_'))
            {
                end++;
            }
            if (end > 0) return rest[..end].ToLowerInvariant();
        }

        // BOM detection.
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return "utf-8";
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return "utf-16";
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return "utf-16BE";

        return "utf-8";
    }

    public static string Decode(byte[] bytes, string charset)
    {
        try
        {
            var encoding = Encoding.GetEncoding(charset);
            var offset = HasBom(bytes, encoding) ? encoding.GetPreamble().Length : 0;
            return encoding.GetString(bytes, offset, bytes.Length - offset);
        }
        catch
        {
            try
            {
                return new UTF8Encoding(false, false).GetString(bytes);
            }
            catch
            {
                return Encoding.Latin1.GetString(bytes);
            }
        }
    }

    private static bool HasBom(byte[] bytes, Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        if (preamble.Length == 0 || bytes.Length < preamble.Length) return false;
        for (var i = 0; i < preamble.Length; i++)
        {
            if (bytes[i] != preamble[i]) return false;
        }
        return true;
    }

    public void Dispose() => _client.Dispose();
}
