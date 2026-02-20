using System.Text.Json.Serialization;
using FabCopilot.Contracts.Enums;

namespace FabCopilot.Contracts.Messages;

public sealed class RagRequest
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("equipmentId")]
    public string EquipmentId { get; set; } = string.Empty;

    [JsonPropertyName("topK")]
    public int TopK { get; set; } = 3;

    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; set; }

    [JsonPropertyName("pipelineMode")]
    public RagPipelineMode PipelineMode { get; set; } = RagPipelineMode.Naive;

    [JsonPropertyName("maxAgenticIterations")]
    public int MaxAgenticIterations { get; set; } = 3;

    [JsonPropertyName("enableGraphLookup")]
    public bool EnableGraphLookup { get; set; }
}
