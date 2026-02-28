using FabCopilot.Contracts.Models;

namespace FabCopilot.McpLogServer.Analysis;

/// <summary>
/// 3-Tier Fusion Engine that integrates evidence from multiple analysis layers
/// to produce ranked diagnostic hypotheses.
///
/// Tier 1: Statistical/ML anomaly detection (TimeSeriesAnalyzer, IsolationForest)
/// Tier 2: Expert knowledge rules (ExpertKnowledgeBase)
/// Tier 3: Document-based causal knowledge (CausalKnowledgeExtractor)
/// </summary>
public sealed class FusionEngine
{
    /// <summary>Weight for Tier 1 (ML/Statistical) evidence.</summary>
    public double Tier1Weight { get; set; } = 0.35;

    /// <summary>Weight for Tier 2 (Expert Rule) evidence.</summary>
    public double Tier2Weight { get; set; } = 0.40;

    /// <summary>Weight for Tier 3 (Document) evidence.</summary>
    public double Tier3Weight { get; set; } = 0.25;

    /// <summary>Minimum confidence to include a hypothesis in the report.</summary>
    public double MinConfidenceThreshold { get; set; } = 0.20;

    /// <summary>
    /// Generates a diagnostic report by fusing evidence from all three tiers.
    /// </summary>
    public DiagnosticReport Diagnose(
        string equipmentId,
        List<AnomalyResult>? anomalies = null,
        List<RulPrediction>? rulPredictions = null,
        List<ExpertRuleMatch>? expertMatches = null,
        List<CausalKnowledgeEntry>? causalEntries = null)
    {
        var hypotheses = new Dictionary<string, HypothesisBuilder>();

        // ── Tier 1: Statistical/ML Evidence ──────────────────────────
        if (anomalies != null)
        {
            foreach (var anomaly in anomalies)
            {
                var key = $"anomaly:{anomaly.SensorId}:{anomaly.Type}";
                var builder = GetOrCreateHypothesis(hypotheses, key,
                    $"{anomaly.SensorId} {anomaly.Type} detected");

                builder.Tier1Evidence.Add(new TierEvidence
                {
                    Source = anomaly.SensorId,
                    Description = anomaly.Description ?? $"{anomaly.Type} at {anomaly.DetectedAt:HH:mm}",
                    Confidence = anomaly.Confidence,
                    Type = "anomaly"
                });
            }
        }

        if (rulPredictions != null)
        {
            foreach (var rul in rulPredictions)
            {
                var urgency = rul.RemainingHours switch
                {
                    <= 0 => "critical",
                    <= 24 => "urgent",
                    <= 168 => "warning",
                    _ => "info"
                };

                var key = $"rul:{urgency}";
                var builder = GetOrCreateHypothesis(hypotheses, key,
                    $"Component approaching end of life ({rul.RemainingHours:F0}h remaining)");

                builder.Tier1Evidence.Add(new TierEvidence
                {
                    Source = "RUL Predictor",
                    Description = rul.Summary,
                    Confidence = rul.Confidence * (rul.RemainingHours <= 24 ? 1.0 : 0.6),
                    Type = "rul"
                });

                builder.RecommendedActions.Add(
                    $"Schedule replacement within {rul.RemainingHours:F0} hours");
            }
        }

        // ── Tier 2: Expert Rule Evidence ─────────────────────────────
        if (expertMatches != null)
        {
            foreach (var match in expertMatches)
            {
                var key = $"expert:{match.Rule.Id}";
                var builder = GetOrCreateHypothesis(hypotheses, key,
                    match.Rule.Hypothesis);

                builder.Tier2Evidence.Add(new TierEvidence
                {
                    Source = $"Rule {match.Rule.Id}: {match.Rule.Name}",
                    Description = $"Matched {match.MatchedTriggers}/{match.TotalTriggers} triggers: " +
                                  string.Join("; ", match.TriggerDetails),
                    Confidence = match.EffectiveConfidence,
                    Type = "expert_rule"
                });

                builder.RecommendedActions.AddRange(match.Rule.Actions);
                if (match.Rule.ManualReference != null)
                    builder.ManualReferences.Add(match.Rule.ManualReference);
            }
        }

        // ── Tier 3: Document Evidence ────────────────────────────────
        if (causalEntries != null)
        {
            foreach (var entry in causalEntries)
            {
                // Try to merge with existing hypothesis by error code or similar cause
                var key = !string.IsNullOrEmpty(entry.ErrorCode)
                    ? $"doc:{entry.ErrorCode}"
                    : $"doc:{entry.Id}";

                var builder = GetOrCreateHypothesis(hypotheses, key,
                    !string.IsNullOrEmpty(entry.Cause) ? entry.Cause : entry.Symptom);

                builder.Tier3Evidence.Add(new TierEvidence
                {
                    Source = entry.SourceDocument,
                    Description = $"Document evidence: {entry.Symptom} → {entry.Cause}",
                    Confidence = entry.Confidence,
                    Type = "document"
                });

                if (!string.IsNullOrEmpty(entry.Action))
                    builder.RecommendedActions.Add(entry.Action);

                if (!string.IsNullOrEmpty(entry.SourceSection))
                    builder.ManualReferences.Add($"[{entry.SourceDocument}:{entry.SourceSection}]");
            }
        }

        // ── Cross-Tier Correlation ───────────────────────────────────
        CorrelateAcrossTiers(hypotheses, anomalies, expertMatches, causalEntries);

        // ── Compute Fused Confidence & Rank ──────────────────────────
        var rankedHypotheses = hypotheses.Values
            .Select(b => BuildHypothesis(b))
            .Where(h => h.Confidence >= MinConfidenceThreshold)
            .OrderByDescending(h => h.Confidence)
            .ToList();

        for (var i = 0; i < rankedHypotheses.Count; i++)
            rankedHypotheses[i].Rank = i + 1;

        // ── Build Report ─────────────────────────────────────────────
        var healthStatus = rankedHypotheses.Count == 0 ? "Normal"
            : rankedHypotheses[0].Confidence >= 0.7 ? "Critical"
            : rankedHypotheses[0].Confidence >= 0.5 ? "Warning"
            : "Caution";

        var summary = rankedHypotheses.Count == 0
            ? "No anomalies or issues detected. Equipment operating normally."
            : string.Join(" | ", rankedHypotheses.Take(3)
                .Select(h => $"{h.Hypothesis} ({h.Confidence:P0})"));

        return new DiagnosticReport
        {
            EquipmentId = equipmentId,
            Hypotheses = rankedHypotheses,
            HealthStatus = healthStatus,
            Summary = summary,
            ActiveAnomalyCount = anomalies?.Count ?? 0,
            TriggeredRuleCount = expertMatches?.Count ?? 0,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    // ── Private Helpers ──────────────────────────────────────────────

    private DiagnosticHypothesis BuildHypothesis(HypothesisBuilder builder)
    {
        // Fused confidence = weighted average of max confidence per tier
        var tier1Max = builder.Tier1Evidence.Count > 0
            ? builder.Tier1Evidence.Max(e => e.Confidence) : 0;
        var tier2Max = builder.Tier2Evidence.Count > 0
            ? builder.Tier2Evidence.Max(e => e.Confidence) : 0;
        var tier3Max = builder.Tier3Evidence.Count > 0
            ? builder.Tier3Evidence.Max(e => e.Confidence) : 0;

        // Only count tiers that have evidence
        var activeTiers = 0.0;
        var weightedSum = 0.0;

        if (tier1Max > 0) { weightedSum += tier1Max * Tier1Weight; activeTiers += Tier1Weight; }
        if (tier2Max > 0) { weightedSum += tier2Max * Tier2Weight; activeTiers += Tier2Weight; }
        if (tier3Max > 0) { weightedSum += tier3Max * Tier3Weight; activeTiers += Tier3Weight; }

        var fusedConfidence = activeTiers > 0 ? weightedSum / activeTiers : 0;

        // Boost confidence when multiple tiers agree
        var tierCount = (tier1Max > 0 ? 1 : 0) + (tier2Max > 0 ? 1 : 0) + (tier3Max > 0 ? 1 : 0);
        if (tierCount >= 2) fusedConfidence *= 1.1; // 10% boost for multi-tier
        if (tierCount >= 3) fusedConfidence *= 1.1; // Additional 10% for all-tier
        fusedConfidence = Math.Min(1.0, fusedConfidence);

        return new DiagnosticHypothesis
        {
            Id = builder.Key,
            Hypothesis = builder.Hypothesis,
            Confidence = fusedConfidence,
            Tier1Evidence = builder.Tier1Evidence,
            Tier2Evidence = builder.Tier2Evidence,
            Tier3Evidence = builder.Tier3Evidence,
            RecommendedActions = builder.RecommendedActions.Distinct().ToList(),
            ManualReferences = builder.ManualReferences.Distinct().ToList(),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private void CorrelateAcrossTiers(
        Dictionary<string, HypothesisBuilder> hypotheses,
        List<AnomalyResult>? anomalies,
        List<ExpertRuleMatch>? expertMatches,
        List<CausalKnowledgeEntry>? causalEntries)
    {
        if (anomalies == null || causalEntries == null) return;

        // Link anomalies with document-based causal knowledge
        foreach (var anomaly in anomalies)
        {
            // Find causal entries that mention the same sensor
            var relatedCausal = causalEntries
                .Where(c => c.Symptom.Contains(anomaly.SensorId, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrEmpty(c.ErrorCode) && anomaly.Description != null
                        && anomaly.Description.Contains(c.ErrorCode, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            foreach (var causal in relatedCausal)
            {
                var anomalyKey = $"anomaly:{anomaly.SensorId}:{anomaly.Type}";
                if (hypotheses.TryGetValue(anomalyKey, out var builder))
                {
                    // Add cross-tier document evidence to the anomaly hypothesis
                    builder.Tier3Evidence.Add(new TierEvidence
                    {
                        Source = causal.SourceDocument,
                        Description = $"Correlated: {causal.Cause}",
                        Confidence = causal.Confidence * 0.8, // Slight reduction for indirect correlation
                        Type = "document"
                    });

                    if (!string.IsNullOrEmpty(causal.Action))
                        builder.RecommendedActions.Add(causal.Action);
                }
            }
        }
    }

    private static HypothesisBuilder GetOrCreateHypothesis(
        Dictionary<string, HypothesisBuilder> hypotheses, string key, string hypothesis)
    {
        if (!hypotheses.TryGetValue(key, out var builder))
        {
            builder = new HypothesisBuilder { Key = key, Hypothesis = hypothesis };
            hypotheses[key] = builder;
        }
        return builder;
    }

    private sealed class HypothesisBuilder
    {
        public string Key { get; set; } = "";
        public string Hypothesis { get; set; } = "";
        public List<TierEvidence> Tier1Evidence { get; } = [];
        public List<TierEvidence> Tier2Evidence { get; } = [];
        public List<TierEvidence> Tier3Evidence { get; } = [];
        public List<string> RecommendedActions { get; } = [];
        public List<string> ManualReferences { get; } = [];
    }
}
