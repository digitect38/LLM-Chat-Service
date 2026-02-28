using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Models;

/// <summary>
/// A diagnostic hypothesis with multi-tier evidence from the Fusion Engine.
/// Each hypothesis represents a possible root cause with supporting evidence.
/// </summary>
public sealed class DiagnosticHypothesis
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Brief description of the hypothesis.</summary>
    [JsonPropertyName("hypothesis")]
    public string Hypothesis { get; set; } = "";

    /// <summary>Overall confidence score (0.0~1.0), fused from all tiers.</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    /// <summary>Ranking position (1 = highest confidence).</summary>
    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    /// <summary>Tier 1 evidence: statistical/ML anomaly detection.</summary>
    [JsonPropertyName("tier1_evidence")]
    public List<TierEvidence> Tier1Evidence { get; set; } = [];

    /// <summary>Tier 2 evidence: expert knowledge rules.</summary>
    [JsonPropertyName("tier2_evidence")]
    public List<TierEvidence> Tier2Evidence { get; set; } = [];

    /// <summary>Tier 3 evidence: document-based causal knowledge.</summary>
    [JsonPropertyName("tier3_evidence")]
    public List<TierEvidence> Tier3Evidence { get; set; } = [];

    /// <summary>Recommended corrective actions (prioritized).</summary>
    [JsonPropertyName("recommended_actions")]
    public List<string> RecommendedActions { get; set; } = [];

    /// <summary>Manual references for each action (e.g., "[MNL-CMP-Ch3-S2.1]").</summary>
    [JsonPropertyName("manual_references")]
    public List<string> ManualReferences { get; set; } = [];

    /// <summary>Equipment type this diagnosis applies to.</summary>
    [JsonPropertyName("equipment_type")]
    public string? EquipmentType { get; set; }

    [JsonPropertyName("generated_at")]
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Evidence item from a specific analysis tier.
/// </summary>
public sealed class TierEvidence
{
    /// <summary>Evidence source identifier.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    /// <summary>Human-readable description of the evidence.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>Confidence of this specific evidence (0.0~1.0).</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    /// <summary>Type: "anomaly", "trend", "alarm_pattern", "expert_rule", "document", "rul".</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
}

/// <summary>
/// Full diagnostic report from the Fusion Engine.
/// </summary>
public sealed class DiagnosticReport
{
    [JsonPropertyName("equipment_id")]
    public string EquipmentId { get; set; } = "";

    [JsonPropertyName("generated_at")]
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("hypotheses")]
    public List<DiagnosticHypothesis> Hypotheses { get; set; } = [];

    /// <summary>Overall equipment health status.</summary>
    [JsonPropertyName("health_status")]
    public string HealthStatus { get; set; } = "Normal";

    /// <summary>Summary text combining top hypotheses.</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    /// <summary>Number of active anomalies detected.</summary>
    [JsonPropertyName("active_anomaly_count")]
    public int ActiveAnomalyCount { get; set; }

    /// <summary>Number of triggered expert rules.</summary>
    [JsonPropertyName("triggered_rule_count")]
    public int TriggeredRuleCount { get; set; }
}
