using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Messages;

public sealed class MessageEnvelope<T>
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("traceId")]
    public string TraceId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("equipmentId")]
    public string? EquipmentId { get; set; }

    [JsonPropertyName("payload")]
    public T? Payload { get; set; }

    public static MessageEnvelope<T> Create(string type, T payload, string? equipmentId = null)
    {
        return new MessageEnvelope<T>
        {
            Type = type,
            Payload = payload,
            EquipmentId = equipmentId
        };
    }
}
