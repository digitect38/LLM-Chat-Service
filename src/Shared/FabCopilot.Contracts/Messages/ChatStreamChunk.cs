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

    [JsonPropertyName("citations")]
    public List<CitationInfo>? Citations { get; set; }
}

public sealed class CitationInfo
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("chunkText")]
    public string ChunkText { get; set; } = string.Empty;

    [JsonPropertyName("section")]
    public string? Section { get; set; }

    [JsonPropertyName("score")]
    public float Score { get; set; }
}
