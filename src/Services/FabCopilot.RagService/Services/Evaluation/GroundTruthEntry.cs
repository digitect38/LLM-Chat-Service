using System.Text.Json.Serialization;
using FabCopilot.Contracts.Enums;

namespace FabCopilot.RagService.Services.Evaluation;

/// <summary>
/// A single ground truth entry for RAG evaluation.
/// Contains query, expected documents, expected answer keywords, and metadata.
/// </summary>
public sealed class GroundTruthEntry
{
    /// <summary>Unique identifier for this test case.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>The user query (Korean or English).</summary>
    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    /// <summary>Language of the query ("ko" or "en").</summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = "ko";

    /// <summary>Expected query intent classification.</summary>
    [JsonPropertyName("intent")]
    public string Intent { get; set; } = "General";

    /// <summary>Expected document filenames that should be retrieved (at least one must appear in top-K).</summary>
    [JsonPropertyName("expected_docs")]
    public List<string> ExpectedDocs { get; set; } = [];

    /// <summary>Expected keywords that should appear in retrieved chunks.</summary>
    [JsonPropertyName("expected_keywords")]
    public List<string> ExpectedKeywords { get; set; } = [];

    /// <summary>Expected answer content (for future answer quality evaluation).</summary>
    [JsonPropertyName("expected_answer")]
    public string ExpectedAnswer { get; set; } = "";

    /// <summary>Equipment type this query is associated with.</summary>
    [JsonPropertyName("equipment_type")]
    public string EquipmentType { get; set; } = "CMP";

    /// <summary>Difficulty level for stratified analysis.</summary>
    [JsonPropertyName("difficulty")]
    public string Difficulty { get; set; } = "normal";
}

/// <summary>
/// Complete ground truth dataset for RAG evaluation.
/// </summary>
public sealed class GroundTruthDataset
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("equipment_type")]
    public string EquipmentType { get; set; } = "CMP";

    [JsonPropertyName("entries")]
    public List<GroundTruthEntry> Entries { get; set; } = [];
}
