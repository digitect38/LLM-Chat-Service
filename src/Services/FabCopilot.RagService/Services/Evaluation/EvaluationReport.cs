using System.Text.Json.Serialization;

namespace FabCopilot.RagService.Services.Evaluation;

/// <summary>
/// Aggregated evaluation results for a single evaluation run.
/// </summary>
public sealed class EvaluationReport
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("pipeline_mode")]
    public string PipelineMode { get; set; } = "";

    [JsonPropertyName("total_queries")]
    public int TotalQueries { get; set; }

    [JsonPropertyName("k")]
    public int K { get; set; } = 10;

    // ── Aggregate metrics ──────────────────────────────────────────

    [JsonPropertyName("recall_at_k")]
    public double RecallAtK { get; set; }

    [JsonPropertyName("mrr_at_k")]
    public double MrrAtK { get; set; }

    [JsonPropertyName("ndcg_at_k")]
    public double NdcgAtK { get; set; }

    [JsonPropertyName("precision_at_k")]
    public double PrecisionAtK { get; set; }

    [JsonPropertyName("hit_rate_at_k")]
    public double HitRateAtK { get; set; }

    [JsonPropertyName("map_at_k")]
    public double MapAtK { get; set; }

    // ── Intent-specific breakdown ──────────────────────────────────

    [JsonPropertyName("by_intent")]
    public Dictionary<string, IntentMetrics> ByIntent { get; set; } = new();

    // ── Language-specific breakdown ─────────────────────────────────

    [JsonPropertyName("by_language")]
    public Dictionary<string, LanguageMetrics> ByLanguage { get; set; } = new();

    // ── Per-query details ──────────────────────────────────────────

    [JsonPropertyName("query_results")]
    public List<QueryEvaluationResult> QueryResults { get; set; } = [];

    // ── Pass/Fail thresholds ───────────────────────────────────────

    [JsonPropertyName("thresholds")]
    public EvaluationThresholds Thresholds { get; set; } = new();

    [JsonPropertyName("passed")]
    public bool Passed => RecallAtK >= Thresholds.MinRecallAtK
                          && MrrAtK >= Thresholds.MinMrrAtK
                          && NdcgAtK >= Thresholds.MinNdcgAtK;
}

public sealed class IntentMetrics
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("recall_at_k")]
    public double RecallAtK { get; set; }

    [JsonPropertyName("mrr_at_k")]
    public double MrrAtK { get; set; }

    [JsonPropertyName("ndcg_at_k")]
    public double NdcgAtK { get; set; }
}

public sealed class LanguageMetrics
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("recall_at_k")]
    public double RecallAtK { get; set; }

    [JsonPropertyName("mrr_at_k")]
    public double MrrAtK { get; set; }

    [JsonPropertyName("ndcg_at_k")]
    public double NdcgAtK { get; set; }
}

public sealed class QueryEvaluationResult
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("intent")]
    public string Intent { get; set; } = "";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "";

    [JsonPropertyName("expected_docs")]
    public List<string> ExpectedDocs { get; set; } = [];

    [JsonPropertyName("retrieved_docs")]
    public List<string> RetrievedDocs { get; set; } = [];

    [JsonPropertyName("recall")]
    public double Recall { get; set; }

    [JsonPropertyName("mrr")]
    public double Mrr { get; set; }

    [JsonPropertyName("ndcg")]
    public double Ndcg { get; set; }

    [JsonPropertyName("hit")]
    public bool Hit { get; set; }

    [JsonPropertyName("keyword_coverage")]
    public double KeywordCoverage { get; set; }
}

public sealed class EvaluationThresholds
{
    [JsonPropertyName("min_recall_at_k")]
    public double MinRecallAtK { get; set; } = 0.80;

    [JsonPropertyName("min_mrr_at_k")]
    public double MinMrrAtK { get; set; } = 0.60;

    [JsonPropertyName("min_ndcg_at_k")]
    public double MinNdcgAtK { get; set; } = 0.60;
}
