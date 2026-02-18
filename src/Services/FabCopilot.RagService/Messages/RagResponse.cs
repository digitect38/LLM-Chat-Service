namespace FabCopilot.RagService.Messages;

public sealed class RagResponse
{
    public string ConversationId { get; set; } = string.Empty;
    public List<RetrievalResult> Results { get; set; } = [];
}

public sealed class RetrievalResult
{
    public string DocumentId { get; set; } = string.Empty;
    public string ChunkText { get; set; } = string.Empty;
    public float Score { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}
