using System.Text.Json;
using Sirius.Engine.Models;

namespace Sirius.Engine.Serialization;

public static class SiriusJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    public static string SerializePage(PageResponse response) => Serialize(response);
}

/// <summary>Envelope used by the stdin/stdout protocol between Python and Sirius.Host.</summary>
public sealed class HostReply
{
    public int Id { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public PageResponse? Result { get; set; }
    public EngineInfo? Info { get; set; }
}

public sealed class EngineInfo
{
    public string Product { get; set; } = "Sirius";
    public string Version { get; set; } = "0.0.1";
    public string Engine { get; set; } = "Sirius.Engine";
    public string Runtime { get; set; } = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
    public string SearchEngine { get; set; } = "DuckDuckGo";
    public string SearchEndpoint { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string HomeUrl { get; set; } = "sirius://home";
    public DateTime StartedAt { get; set; }
}
