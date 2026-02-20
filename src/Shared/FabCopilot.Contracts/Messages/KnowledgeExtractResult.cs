using System.Text.Json.Serialization;
using FabCopilot.Contracts.Models;

namespace FabCopilot.Contracts.Messages;

public sealed class KnowledgeExtractResult
{
    [JsonPropertyName("conversationId")]
    public string ConversationId { get; set; } = string.Empty;

    [JsonPropertyName("candidates")]
    public List<KnowledgeObject> Candidates { get; set; } = [];
}
