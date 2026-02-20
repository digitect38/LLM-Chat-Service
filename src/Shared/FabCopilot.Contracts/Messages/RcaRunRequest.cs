using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Messages;

public sealed class RcaRunRequest
{
    [JsonPropertyName("equipmentId")]
    public string EquipmentId { get; set; } = string.Empty;

    [JsonPropertyName("alarmCode")]
    public string AlarmCode { get; set; } = string.Empty;

    [JsonPropertyName("triggeredAt")]
    public DateTimeOffset TriggeredAt { get; set; }

    [JsonPropertyName("question")]
    public string? Question { get; set; }

    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; set; }
}
