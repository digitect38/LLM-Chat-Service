using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Models;

public sealed class GraphStats
{
    [JsonPropertyName("entityCount")]
    public int EntityCount { get; set; }

    [JsonPropertyName("relationCount")]
    public int RelationCount { get; set; }

    [JsonPropertyName("entitiesByType")]
    public Dictionary<string, int> EntitiesByType { get; set; } = new();
}
