using System.Text.Json;
using System.Text.Json.Serialization;

namespace FabCopilot.WebClient.Models;

public sealed class JsonLogEntry
{
    [JsonPropertyName("@t")] public string? Timestamp { get; set; }
    [JsonPropertyName("@mt")] public string? MessageTemplate { get; set; }
    [JsonPropertyName("@l")] public string? Level { get; set; }
    [JsonPropertyName("@x")] public string? Exception { get; set; }
    [JsonPropertyName("ServiceName")] public string? ServiceName { get; set; }
    [JsonPropertyName("CorrelationId")] public string? CorrelationId { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    public string DisplayLevel => Level switch
    {
        "Fatal" or "Critical" => "FTL",
        "Error" => "ERR",
        "Warning" => "WRN",
        "Debug" => "DBG",
        "Verbose" or "Trace" => "VRB",
        _ => "INF"
    };

    public string RenderedMessage
    {
        get
        {
            if (string.IsNullOrEmpty(MessageTemplate)) return string.Empty;
            if (Extra is null || Extra.Count == 0) return MessageTemplate;

            var result = MessageTemplate;
            foreach (var (key, value) in Extra)
            {
                var placeholder = "{" + key + "}";
                if (result.Contains(placeholder))
                {
                    result = result.Replace(placeholder, value.ToString());
                }
            }
            return result;
        }
    }

    public string DisplayTime
    {
        get
        {
            if (DateTimeOffset.TryParse(Timestamp, out var dto))
                return dto.ToLocalTime().ToString("HH:mm:ss.fff");
            return Timestamp ?? "";
        }
    }
}
