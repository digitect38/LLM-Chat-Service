using System.Text.Json.Serialization;
using FabCopilot.Contracts.Enums;

namespace FabCopilot.Contracts.Models;

public sealed class KnowledgeObject
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("equipment")]
    public string? Equipment { get; set; }

    [JsonPropertyName("module")]
    public string? Module { get; set; }

    [JsonPropertyName("symptom")]
    public string? Symptom { get; set; }

    [JsonPropertyName("rootCause")]
    public string? RootCause { get; set; }

    [JsonPropertyName("solution")]
    public string? Solution { get; set; }

    [JsonPropertyName("evidence")]
    public List<string>? Evidence { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("status")]
    public KnowledgeStatus Status { get; set; } = KnowledgeStatus.Draft;

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("approvedBy")]
    public string? ApprovedBy { get; set; }

    [JsonPropertyName("approvedAt")]
    public DateTimeOffset? ApprovedAt { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
