using System.Text.Json;
using System.Text.Json.Serialization;
using FabCopilot.Contracts.Enums;

namespace FabCopilot.Contracts.Models;

public sealed class LogRecord
{
    [JsonPropertyName("ts")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("equipmentId")]
    public string EquipmentId { get; set; } = string.Empty;

    [JsonPropertyName("module")]
    public string? Module { get; set; }

    [JsonPropertyName("level")]
    public EquipmentLogLevel Level { get; set; }

    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("event")]
    public string? Event { get; set; }

    [JsonPropertyName("fields")]
    public Dictionary<string, JsonElement>? Fields { get; set; }

    [JsonPropertyName("raw")]
    public string? Raw { get; set; }
}
