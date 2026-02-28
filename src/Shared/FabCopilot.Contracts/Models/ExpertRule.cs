using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Models;

/// <summary>
/// A structured expert knowledge rule for diagnostic reasoning.
/// Represents domain expert knowledge: trigger condition → hypothesis → recommended action.
/// </summary>
public sealed class ExpertRule
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Display name for the rule.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Equipment type this rule applies to (e.g., "CMP").</summary>
    [JsonPropertyName("equipment_type")]
    public string EquipmentType { get; set; } = "";

    /// <summary>Specific equipment model (empty = all models of this type).</summary>
    [JsonPropertyName("equipment_model")]
    public string EquipmentModel { get; set; } = "";

    /// <summary>Trigger conditions that activate this rule.</summary>
    [JsonPropertyName("triggers")]
    public List<RuleTrigger> Triggers { get; set; } = [];

    /// <summary>Diagnostic hypothesis when triggers match.</summary>
    [JsonPropertyName("hypothesis")]
    public string Hypothesis { get; set; } = "";

    /// <summary>Root cause explanation.</summary>
    [JsonPropertyName("root_cause")]
    public string RootCause { get; set; } = "";

    /// <summary>Recommended corrective actions (ordered by priority).</summary>
    [JsonPropertyName("actions")]
    public List<string> Actions { get; set; } = [];

    /// <summary>Confidence score (0.0~1.0), updated by verification loop.</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; } = 0.5;

    /// <summary>Number of times this rule was triggered.</summary>
    [JsonPropertyName("hit_count")]
    public int HitCount { get; set; }

    /// <summary>Number of times the rule was confirmed correct by feedback.</summary>
    [JsonPropertyName("confirm_count")]
    public int ConfirmCount { get; set; }

    /// <summary>Rule version for tracking updates.</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>Whether this rule is currently active.</summary>
    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    /// <summary>Who created/authored this rule.</summary>
    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Manual cross-reference (e.g., "[MNL-CMP-Ch3-S2.1-{Line:45-67}]").</summary>
    [JsonPropertyName("manual_reference")]
    public string? ManualReference { get; set; }

    /// <summary>Precision = confirm_count / hit_count.</summary>
    [JsonIgnore]
    public double Precision => HitCount > 0 ? (double)ConfirmCount / HitCount : 0;
}

/// <summary>
/// A trigger condition for an expert rule.
/// </summary>
public sealed class RuleTrigger
{
    /// <summary>Type of trigger: "sensor", "alarm", "error_code", "pattern".</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    /// <summary>Sensor ID or alarm code to monitor.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    /// <summary>Comparison operator: "gt", "lt", "eq", "between", "contains", "matches".</summary>
    [JsonPropertyName("operator")]
    public string Operator { get; set; } = "";

    /// <summary>Threshold value (for numeric comparisons).</summary>
    [JsonPropertyName("value")]
    public double? Value { get; set; }

    /// <summary>Upper bound (for "between" operator).</summary>
    [JsonPropertyName("value_upper")]
    public double? ValueUpper { get; set; }

    /// <summary>String pattern (for "contains" or "matches" operators).</summary>
    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }

    /// <summary>Duration the condition must persist (seconds). 0 = instant trigger.</summary>
    [JsonPropertyName("duration_seconds")]
    public int DurationSeconds { get; set; }
}
