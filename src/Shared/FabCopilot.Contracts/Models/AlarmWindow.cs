using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Models;

public sealed class AlarmWindow
{
    [JsonPropertyName("alarmCode")]
    public string AlarmCode { get; set; } = string.Empty;

    [JsonPropertyName("equipmentId")]
    public string EquipmentId { get; set; } = string.Empty;

    [JsonPropertyName("triggeredAt")]
    public DateTimeOffset TriggeredAt { get; set; }

    [JsonPropertyName("windowBeforeSec")]
    public int WindowBeforeSec { get; set; } = 300;

    [JsonPropertyName("windowAfterSec")]
    public int WindowAfterSec { get; set; } = 300;

    [JsonPropertyName("recordsRef")]
    public string? RecordsRef { get; set; }
}
