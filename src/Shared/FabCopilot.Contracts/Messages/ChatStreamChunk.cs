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
    [JsonPropertyName("citationId")]
    public string CitationId { get; set; } = string.Empty;

    [JsonPropertyName("docId")]
    public string DocId { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("chunkId")]
    public string? ChunkId { get; set; }

    [JsonPropertyName("chunkText")]
    public string ChunkText { get; set; } = string.Empty;

    [JsonPropertyName("section")]
    public string? Section { get; set; }

    [JsonPropertyName("page")]
    public int? Page { get; set; }

    [JsonPropertyName("charOffsetStart")]
    public int? CharOffsetStart { get; set; }

    [JsonPropertyName("charOffsetEnd")]
    public int? CharOffsetEnd { get; set; }

    [JsonPropertyName("pdfUrl")]
    public string? PdfUrl { get; set; }

    [JsonPropertyName("parentContext")]
    public string? ParentContext { get; set; }

    [JsonPropertyName("score")]
    public float Score { get; set; }

    [JsonPropertyName("highlightType")]
    public string? HighlightType { get; set; }

    [JsonPropertyName("revision")]
    public string? Revision { get; set; }
}
