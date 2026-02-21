using System.Text.Json.Serialization;
using FabCopilot.Contracts.Enums;

namespace FabCopilot.Contracts.Messages;

public sealed class RagResponse
{
    [JsonPropertyName("conversationId")]
    public string ConversationId { get; set; } = string.Empty;

    [JsonPropertyName("results")]
    public List<RetrievalResult> Results { get; set; } = [];

    [JsonPropertyName("pipelineMode")]
    public RagPipelineMode PipelineMode { get; set; }

    [JsonPropertyName("iterationCount")]
    public int IterationCount { get; set; }

    [JsonPropertyName("rewrittenQuery")]
    public string? RewrittenQuery { get; set; }

    [JsonPropertyName("queryIntent")]
    public QueryIntent QueryIntent { get; set; }

    [JsonPropertyName("maxScore")]
    public float MaxScore { get; set; }

    [JsonPropertyName("isConfident")]
    public bool IsConfident { get; set; } = true;
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

    [JsonPropertyName("graphContext")]
    public string? GraphContext { get; set; }
}
