using System.Text.Json.Serialization;
using FabCopilot.Contracts.Models;

namespace FabCopilot.Contracts.Messages;

public sealed class ChatRequest
{
    [JsonPropertyName("conversationId")]
    public string ConversationId { get; set; } = string.Empty;

    [JsonPropertyName("equipmentId")]
    public string EquipmentId { get; set; } = string.Empty;

    [JsonPropertyName("userMessage")]
    public string UserMessage { get; set; } = string.Empty;

    [JsonPropertyName("context")]
    public EquipmentContext? Context { get; set; }
}
