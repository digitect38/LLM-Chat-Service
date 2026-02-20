using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Messages;

public sealed class RagResponse
{
    [JsonPropertyName("conversationId")]
    public string ConversationId { get; set; } = string.Empty;

    [JsonPropertyName("results")]
    public List<RetrievalResult> Results { get; set; } = [];
}

public sealed class RetrievalResult
{
    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("chunkText")]
    public string ChunkText { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public float Score { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = new();
}
