using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Models;

public sealed class GraphEntity
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("properties")]
    public Dictionary<string, string> Properties { get; set; } = new();
}
