using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Models;

/// <summary>
/// A causal knowledge triple extracted from technical documents.
/// Represents: Error/Symptom → Root Cause → Recommended Action.
/// </summary>
public sealed class CausalKnowledgeEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Error code or alarm code (e.g., "E-1023", "A100").</summary>
    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; set; }

    /// <summary>Observable symptom (e.g., "platen vibration exceeds threshold").</summary>
    [JsonPropertyName("symptom")]
    public string Symptom { get; set; } = "";

    /// <summary>Root cause or diagnosis (e.g., "worn conditioner disc").</summary>
    [JsonPropertyName("cause")]
    public string Cause { get; set; } = "";

    /// <summary>Recommended corrective action.</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    /// <summary>Source document ID.</summary>
    [JsonPropertyName("source_document")]
    public string SourceDocument { get; set; } = "";

    /// <summary>Section/chapter in the source document.</summary>
    [JsonPropertyName("source_section")]
    public string? SourceSection { get; set; }

    /// <summary>Equipment type this applies to (e.g., "CMP").</summary>
    [JsonPropertyName("equipment_type")]
    public string? EquipmentType { get; set; }

    /// <summary>Confidence score (0.0 ~ 1.0) based on extraction quality.</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; } = 0.5;

    /// <summary>When this entry was extracted.</summary>
    [JsonPropertyName("extracted_at")]
    public DateTimeOffset ExtractedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Cold-start RUL estimate derived from document-mentioned component lifespans.
/// </summary>
public sealed class ColdStartRulEstimate
{
    [JsonPropertyName("component_name")]
    public string ComponentName { get; set; } = "";

    /// <summary>Expected life in operating hours (from manual).</summary>
    [JsonPropertyName("expected_life_hours")]
    public double ExpectedLifeHours { get; set; }

    /// <summary>Expected life in wafer count (if applicable).</summary>
    [JsonPropertyName("expected_life_wafers")]
    public int? ExpectedLifeWafers { get; set; }

    /// <summary>Replacement trigger condition from manual.</summary>
    [JsonPropertyName("replacement_trigger")]
    public string? ReplacementTrigger { get; set; }

    /// <summary>Source document and section.</summary>
    [JsonPropertyName("source_document")]
    public string SourceDocument { get; set; } = "";

    [JsonPropertyName("source_section")]
    public string? SourceSection { get; set; }

    /// <summary>Equipment type.</summary>
    [JsonPropertyName("equipment_type")]
    public string? EquipmentType { get; set; }
}
