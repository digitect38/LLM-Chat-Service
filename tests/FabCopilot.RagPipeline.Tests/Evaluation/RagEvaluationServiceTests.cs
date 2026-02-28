using FabCopilot.RagService.Services.Evaluation;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Evaluation;

/// <summary>
/// Integration tests for the RAG evaluation service.
/// Runs BM25-based evaluation against the actual knowledge docs
/// and ground truth dataset.
/// </summary>
public class RagEvaluationServiceTests
{
    private static readonly string DocsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "Services", "FabCopilot.RagService", "knowledge-docs");

    private static readonly string GroundTruthPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "Services", "FabCopilot.RagService", "rag-evaluation-groundtruth.json");

    [Fact]
    public void LoadGroundTruth_ParsesDatasetCorrectly()
    {
        var dataset = RagEvaluationService.LoadGroundTruth(GroundTruthPath);

        dataset.Should().NotBeNull();
        dataset.Version.Should().Be("1.0");
        dataset.EquipmentType.Should().Be("CMP");
        dataset.Entries.Should().HaveCountGreaterOrEqualTo(100,
            because: "ground truth should have 100+ entries for comprehensive evaluation");
    }

    [Fact]
    public void LoadGroundTruth_HasAllIntents()
    {
        var dataset = RagEvaluationService.LoadGroundTruth(GroundTruthPath);
        var intents = dataset.Entries.Select(e => e.Intent).Distinct().ToList();

        intents.Should().Contain("Error");
        intents.Should().Contain("Procedure");
        intents.Should().Contain("Part");
        intents.Should().Contain("Definition");
        intents.Should().Contain("Spec");
        intents.Should().Contain("Comparison");
        intents.Should().Contain("General");
    }

    [Fact]
    public void LoadGroundTruth_HasBothLanguages()
    {
        var dataset = RagEvaluationService.LoadGroundTruth(GroundTruthPath);
        var languages = dataset.Entries.Select(e => e.Language).Distinct().ToList();

        languages.Should().Contain("ko");
        languages.Should().Contain("en");
    }

    [Fact]
    public void EvaluateBm25_ProducesValidReport()
    {
        var service = new RagEvaluationService(NullLogger<RagEvaluationService>.Instance);
        var dataset = RagEvaluationService.LoadGroundTruth(GroundTruthPath);

        var report = service.EvaluateBm25(dataset, DocsPath, k: 10);

        report.Should().NotBeNull();
        report.TotalQueries.Should().BeGreaterThan(0);
        report.PipelineMode.Should().Be("BM25");
        report.K.Should().Be(10);

        // Metrics should be in valid range [0, 1]
        report.RecallAtK.Should().BeInRange(0, 1);
        report.MrrAtK.Should().BeInRange(0, 1);
        report.NdcgAtK.Should().BeInRange(0, 1);
        report.PrecisionAtK.Should().BeInRange(0, 1);
        report.HitRateAtK.Should().BeInRange(0, 1);
        report.MapAtK.Should().BeInRange(0, 1);
    }

    [Fact]
    public void EvaluateBm25_HasIntentBreakdown()
    {
        var service = new RagEvaluationService(NullLogger<RagEvaluationService>.Instance);
        var dataset = RagEvaluationService.LoadGroundTruth(GroundTruthPath);

        var report = service.EvaluateBm25(dataset, DocsPath, k: 10);

        report.ByIntent.Should().NotBeEmpty("evaluation should break down by intent");
        foreach (var (_, metrics) in report.ByIntent)
        {
            metrics.Count.Should().BeGreaterThan(0);
            metrics.RecallAtK.Should().BeInRange(0, 1);
            metrics.MrrAtK.Should().BeInRange(0, 1);
            metrics.NdcgAtK.Should().BeInRange(0, 1);
        }
    }

    [Fact]
    public void EvaluateBm25_HasLanguageBreakdown()
    {
        var service = new RagEvaluationService(NullLogger<RagEvaluationService>.Instance);
        var dataset = RagEvaluationService.LoadGroundTruth(GroundTruthPath);

        var report = service.EvaluateBm25(dataset, DocsPath, k: 10);

        report.ByLanguage.Should().ContainKey("ko");
        report.ByLanguage.Should().ContainKey("en");
    }

    [Fact]
    public void EvaluateBm25_HasPerQueryResults()
    {
        var service = new RagEvaluationService(NullLogger<RagEvaluationService>.Instance);
        var dataset = RagEvaluationService.LoadGroundTruth(GroundTruthPath);

        var report = service.EvaluateBm25(dataset, DocsPath, k: 10);

        report.QueryResults.Should().HaveCount(dataset.Entries.Count);
        foreach (var qr in report.QueryResults)
        {
            qr.Id.Should().NotBeNullOrEmpty();
            qr.Query.Should().NotBeNullOrEmpty();
            qr.Recall.Should().BeInRange(0, 1);
            qr.Mrr.Should().BeInRange(0, 1);
            qr.Ndcg.Should().BeInRange(0, 1);
        }
    }

    [Fact]
    public void EvaluateBm25_HitRateIsReasonable()
    {
        var service = new RagEvaluationService(NullLogger<RagEvaluationService>.Instance);
        var dataset = RagEvaluationService.LoadGroundTruth(GroundTruthPath);

        var report = service.EvaluateBm25(dataset, DocsPath, k: 10);

        // BM25 should at least hit some docs for well-crafted golden queries
        report.HitRateAtK.Should().BeGreaterThan(0.5,
            because: "BM25 should find at least one relevant doc for majority of golden queries");
    }

    [Fact]
    public void FormatSummary_ProducesReadableOutput()
    {
        var service = new RagEvaluationService(NullLogger<RagEvaluationService>.Instance);
        var dataset = RagEvaluationService.LoadGroundTruth(GroundTruthPath);
        var report = service.EvaluateBm25(dataset, DocsPath, k: 10);

        var summary = RagEvaluationService.FormatSummary(report);

        summary.Should().NotBeNullOrEmpty();
        summary.Should().Contain("RAG Evaluation Report");
        summary.Should().Contain("Recall@10");
        summary.Should().Contain("MRR@10");
        summary.Should().Contain("NDCG@10");
    }

    [Fact]
    public void ToJson_ProducesValidJson()
    {
        var service = new RagEvaluationService(NullLogger<RagEvaluationService>.Instance);
        var dataset = RagEvaluationService.LoadGroundTruth(GroundTruthPath);
        var report = service.EvaluateBm25(dataset, DocsPath, k: 10);

        var json = RagEvaluationService.ToJson(report);

        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("\"recall_at_k\"");
        json.Should().Contain("\"mrr_at_k\"");
        json.Should().Contain("\"ndcg_at_k\"");
        json.Should().Contain("\"by_intent\"");
    }

    [Fact]
    public void EvaluateBm25_CustomThresholds_AffectsPassFail()
    {
        var service = new RagEvaluationService(NullLogger<RagEvaluationService>.Instance);
        var dataset = RagEvaluationService.LoadGroundTruth(GroundTruthPath);

        // Very low thresholds → should pass
        var easyThresholds = new EvaluationThresholds
        {
            MinRecallAtK = 0.01,
            MinMrrAtK = 0.01,
            MinNdcgAtK = 0.01
        };
        var easyReport = service.EvaluateBm25(dataset, DocsPath, k: 10, easyThresholds);
        easyReport.Passed.Should().BeTrue("with very low thresholds, evaluation should pass");

        // Impossibly high thresholds → should fail
        var hardThresholds = new EvaluationThresholds
        {
            MinRecallAtK = 0.999,
            MinMrrAtK = 0.999,
            MinNdcgAtK = 0.999
        };
        var hardReport = service.EvaluateBm25(dataset, DocsPath, k: 10, hardThresholds);
        hardReport.Passed.Should().BeFalse("with impossibly high thresholds, evaluation should fail");
    }
}
