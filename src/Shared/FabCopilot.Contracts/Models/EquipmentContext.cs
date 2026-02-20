using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Models;

public sealed class EquipmentContext
{
    [JsonPropertyName("equipmentId")]
    public string EquipmentId { get; set; } = string.Empty;

    [JsonPropertyName("module")]
    public string? Module { get; set; }

    [JsonPropertyName("recipe")]
    public string? Recipe { get; set; }

    [JsonPropertyName("processState")]
    public string? ProcessState { get; set; }

    [JsonPropertyName("recentAlarms")]
    public List<string>? RecentAlarms { get; set; }
}
