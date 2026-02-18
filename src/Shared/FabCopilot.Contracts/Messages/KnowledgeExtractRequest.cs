using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Messages;

public sealed class KnowledgeExtractRequest
{
    [JsonPropertyName("conversationId")]
    public string ConversationId { get; set; } = string.Empty;

    [JsonPropertyName("equipmentId")]
    public string EquipmentId { get; set; } = string.Empty;
}
