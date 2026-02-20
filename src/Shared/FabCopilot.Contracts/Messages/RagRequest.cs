using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Messages;

public sealed class RagRequest
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("equipmentId")]
    public string EquipmentId { get; set; } = string.Empty;

    [JsonPropertyName("topK")]
    public int TopK { get; set; } = 3;

    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; set; }
}
