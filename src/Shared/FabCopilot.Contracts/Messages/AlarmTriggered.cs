using System.Text.Json.Serialization;
using FabCopilot.Contracts.Enums;

namespace FabCopilot.Contracts.Messages;

public sealed class AlarmTriggered
{
    [JsonPropertyName("equipmentId")]
    public string EquipmentId { get; set; } = string.Empty;

    [JsonPropertyName("alarmCode")]
    public string AlarmCode { get; set; } = string.Empty;

    [JsonPropertyName("triggeredAt")]
    public DateTimeOffset TriggeredAt { get; set; }

    [JsonPropertyName("severity")]
    public AlarmSeverity Severity { get; set; }

    [JsonPropertyName("module")]
    public string? Module { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
