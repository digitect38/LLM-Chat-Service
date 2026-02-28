using FabCopilot.Contracts.Models;
using FabCopilot.McpLogServer.Analysis;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Analysis;

/// <summary>
/// Tests for the 3-Tier Fusion Engine diagnostic system.
/// </summary>
public class FusionEngineTests
{
    // ── Basic Diagnosis ──────────────────────────────────────────────

    [Fact]
    public void Diagnose_NoEvidence_ReturnsNormalReport()
    {
        var engine = new FusionEngine();
        var report = engine.Diagnose("CMP-01");

        report.EquipmentId.Should().Be("CMP-01");
        report.HealthStatus.Should().Be("Normal");
        report.Hypotheses.Should().BeEmpty();
        report.Summary.Should().Contain("No anomalies");
    }

    [Fact]
    public void Diagnose_WithAnomalies_CreatesHypotheses()
    {
        var engine = new FusionEngine();
        var anomalies = new List<AnomalyResult>
        {
            new()
            {
                EquipmentId = "CMP-01", SensorId = "platen_rpm",
                Type = AnomalyType.SpikeUp, Severity = AnomalySeverity.Anomaly,
                Confidence = 0.85, DetectedAt = DateTimeOffset.UtcNow,
                Value = 95, Description = "Spike detected"
            }
        };

        var report = engine.Diagnose("CMP-01", anomalies: anomalies);

        report.Hypotheses.Should().HaveCountGreaterOrEqualTo(1);
        report.ActiveAnomalyCount.Should().Be(1);
        report.Hypotheses[0].Tier1Evidence.Should().NotBeEmpty();
    }

    [Fact]
    public void Diagnose_WithExpertRules_IncludesActions()
    {
        var engine = new FusionEngine();
        var expertMatches = new List<ExpertRuleMatch>
        {
            new()
            {
                Rule = new ExpertRule
                {
                    Id = "ER-0001", Name = "Slurry Flow Low",
                    Hypothesis = "슬러리 라인 막힘",
                    Actions = ["필터 교체", "라인 퍼지"],
                    Confidence = 0.8,
                    ManualReference = "[MNL-CMP-Ch5]"
                },
                MatchedTriggers = 2, TotalTriggers = 2, MatchRatio = 1.0,
                TriggerDetails = ["slurry_flow=130 lt 140", "Alarm A201 active"],
                EffectiveConfidence = 0.8
            }
        };

        var report = engine.Diagnose("CMP-01", expertMatches: expertMatches);

        report.Hypotheses.Should().HaveCount(1);
        report.Hypotheses[0].RecommendedActions.Should().Contain("필터 교체");
        report.Hypotheses[0].ManualReferences.Should().Contain("[MNL-CMP-Ch5]");
        report.TriggeredRuleCount.Should().Be(1);
    }

    [Fact]
    public void Diagnose_WithDocumentEvidence_IncludesSourceRef()
    {
        var engine = new FusionEngine();
        var causalEntries = new List<CausalKnowledgeEntry>
        {
            new()
            {
                Id = "causal:1", ErrorCode = "E-1023",
                Symptom = "Platen vibration", Cause = "Worn conditioner disc",
                Action = "Replace disc", SourceDocument = "cmp-manual.md",
                SourceSection = "Ch3", Confidence = 0.7
            }
        };

        var report = engine.Diagnose("CMP-01", causalEntries: causalEntries);

        report.Hypotheses.Should().HaveCount(1);
        report.Hypotheses[0].Tier3Evidence.Should().NotBeEmpty();
    }

    // ── Multi-Tier Fusion ────────────────────────────────────────────

    [Fact]
    public void Diagnose_MultiTierEvidence_BoostsConfidence()
    {
        var engine = new FusionEngine();

        var anomalies = new List<AnomalyResult>
        {
            new()
            {
                SensorId = "slurry_flow", Type = AnomalyType.SpikeDown,
                Confidence = 0.7, DetectedAt = DateTimeOffset.UtcNow,
                Description = "Flow drop"
            }
        };

        var expertMatches = new List<ExpertRuleMatch>
        {
            new()
            {
                Rule = new ExpertRule
                {
                    Id = "ER-001", Hypothesis = "Filter clog",
                    Actions = ["Replace filter"], Confidence = 0.8
                },
                MatchedTriggers = 1, TotalTriggers = 1, MatchRatio = 1.0,
                TriggerDetails = ["slurry_flow lt 140"], EffectiveConfidence = 0.8
            }
        };

        // Single-tier diagnosis
        var singleReport = engine.Diagnose("CMP-01", anomalies: anomalies);
        var singleConfidence = singleReport.Hypotheses.Count > 0
            ? singleReport.Hypotheses.Max(h => h.Confidence) : 0;

        // Multi-tier diagnosis: anomaly + expert rule
        var multiReport = engine.Diagnose("CMP-01", anomalies: anomalies, expertMatches: expertMatches);

        multiReport.Hypotheses.Should().HaveCountGreaterOrEqualTo(1);
        // Expert rule match provides higher confidence
        multiReport.Hypotheses.Max(h => h.Confidence).Should().BeGreaterOrEqualTo(singleConfidence);
    }

    // ── Ranking ──────────────────────────────────────────────────────

    [Fact]
    public void Diagnose_RanksHypothesesByConfidence()
    {
        var engine = new FusionEngine();

        var expertMatches = new List<ExpertRuleMatch>
        {
            new()
            {
                Rule = new ExpertRule { Id = "ER-001", Hypothesis = "Low confidence", Confidence = 0.3 },
                MatchedTriggers = 1, TotalTriggers = 1, MatchRatio = 1.0,
                TriggerDetails = ["t1"], EffectiveConfidence = 0.3
            },
            new()
            {
                Rule = new ExpertRule { Id = "ER-002", Hypothesis = "High confidence", Confidence = 0.9 },
                MatchedTriggers = 1, TotalTriggers = 1, MatchRatio = 1.0,
                TriggerDetails = ["t2"], EffectiveConfidence = 0.9
            }
        };

        var report = engine.Diagnose("CMP-01", expertMatches: expertMatches);

        report.Hypotheses[0].Confidence.Should().BeGreaterThan(report.Hypotheses[1].Confidence);
        report.Hypotheses[0].Rank.Should().Be(1);
        report.Hypotheses[1].Rank.Should().Be(2);
    }

    // ── Health Status ────────────────────────────────────────────────

    [Fact]
    public void Diagnose_HighConfidence_SetsCriticalStatus()
    {
        var engine = new FusionEngine();
        var expertMatches = new List<ExpertRuleMatch>
        {
            new()
            {
                Rule = new ExpertRule { Id = "ER-001", Hypothesis = "Critical issue", Confidence = 0.95 },
                MatchedTriggers = 1, TotalTriggers = 1, MatchRatio = 1.0,
                TriggerDetails = ["t1"], EffectiveConfidence = 0.95
            }
        };

        var report = engine.Diagnose("CMP-01", expertMatches: expertMatches);

        report.HealthStatus.Should().Be("Critical");
    }

    // ── RUL Evidence ─────────────────────────────────────────────────

    [Fact]
    public void Diagnose_WithRulPredictions_AddsEvidence()
    {
        var engine = new FusionEngine();
        var rul = new List<RulPrediction>
        {
            new()
            {
                RemainingHours = 20, Confidence = 0.85,
                PredictedFailureTime = DateTimeOffset.UtcNow.AddHours(20),
                CurrentValue = 30, FailureThreshold = 10
            }
        };

        var report = engine.Diagnose("CMP-01", rulPredictions: rul);

        report.Hypotheses.Should().NotBeEmpty();
        report.Hypotheses.Should().Contain(h => h.Tier1Evidence.Any(e => e.Type == "rul"));
    }

    // ── Min Confidence Threshold ─────────────────────────────────────

    [Fact]
    public void Diagnose_BelowMinThreshold_Excluded()
    {
        var engine = new FusionEngine { MinConfidenceThreshold = 0.5 };
        var expertMatches = new List<ExpertRuleMatch>
        {
            new()
            {
                Rule = new ExpertRule { Id = "ER-001", Hypothesis = "Very weak", Confidence = 0.1 },
                MatchedTriggers = 1, TotalTriggers = 3, MatchRatio = 0.33,
                TriggerDetails = ["t1"], EffectiveConfidence = 0.1
            }
        };

        var report = engine.Diagnose("CMP-01", expertMatches: expertMatches);

        report.Hypotheses.Should().BeEmpty("confidence too low for threshold");
    }
}
