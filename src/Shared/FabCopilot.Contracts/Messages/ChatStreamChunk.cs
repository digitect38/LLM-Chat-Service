using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Messages;

public sealed class ChatStreamChunk
{
    [JsonPropertyName("conversationId")]
    public string ConversationId { get; set; } = string.Empty;

    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("isComplete")]
    public bool IsComplete { get; set; }

    [JsonPropertyName("toolCallRef")]
    public string? ToolCallRef { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
