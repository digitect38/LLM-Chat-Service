using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Models;

public sealed class GraphRelation
{
    [JsonPropertyName("sourceId")]
    public string SourceId { get; set; } = string.Empty;

    [JsonPropertyName("targetId")]
    public string TargetId { get; set; } = string.Empty;

    [JsonPropertyName("relationType")]
    public string RelationType { get; set; } = string.Empty;

    [JsonPropertyName("properties")]
    public Dictionary<string, string> Properties { get; set; } = new();
}
