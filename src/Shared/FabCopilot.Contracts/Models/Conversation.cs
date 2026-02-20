using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Models;

public sealed class Conversation
{
    [JsonPropertyName("conversationId")]
    public string ConversationId { get; set; } = string.Empty;

    [JsonPropertyName("equipmentId")]
    public string EquipmentId { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("lastUpdatedAt")]
    public DateTimeOffset LastUpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = [];
}
