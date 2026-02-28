using FabCopilot.RagService.Services.Evaluation;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Evaluation;

/// <summary>
/// Unit tests for RAGAS-style retrieval quality metrics.
/// </summary>
public class RagEvaluationMetricsTests
{
    private static readonly HashSet<string> Relevant = new(["doc-A", "doc-B", "doc-C"]);

    // ── Recall@K ─────────────────────────────────────────────────────

    [Fact]
    public void RecallAtK_AllRelevantInTopK_Returns1()
    {
        var retrieved = new List<string> { "doc-A", "doc-B", "doc-C", "doc-X" };
        RagEvaluationMetrics.RecallAtK(retrieved, Relevant, 4).Should().Be(1.0);
    }

    [Fact]
    public void RecallAtK_SomeRelevantInTopK_ReturnsFraction()
    {
        var retrieved = new List<string> { "doc-X", "doc-A", "doc-Y" };
        // 1 out of 3 relevant found in top-3
        RagEvaluationMetrics.RecallAtK(retrieved, Relevant, 3).Should().BeApproximately(1.0 / 3, 0.001);
    }

    [Fact]
    public void RecallAtK_NoneRelevant_Returns0()
    {
        var retrieved = new List<string> { "doc-X", "doc-Y", "doc-Z" };
        RagEvaluationMetrics.RecallAtK(retrieved, Relevant, 3).Should().Be(0.0);
    }

    [Fact]
    public void RecallAtK_EmptyRelevantSet_Returns0()
    {
        var retrieved = new List<string> { "doc-A" };
        RagEvaluationMetrics.RecallAtK(retrieved, new HashSet<string>(), 3).Should().Be(0.0);
    }

    [Fact]
    public void RecallAtK_RespectsKLimit()
    {
        // doc-B is at position 3 (index 2), but K=2 so it shouldn't count
        var retrieved = new List<string> { "doc-X", "doc-A", "doc-B" };
        RagEvaluationMetrics.RecallAtK(retrieved, Relevant, 2).Should().BeApproximately(1.0 / 3, 0.001);
    }

    // ── Precision@K ──────────────────────────────────────────────────

    [Fact]
    public void PrecisionAtK_AllRelevant_Returns1()
    {
        var retrieved = new List<string> { "doc-A", "doc-B", "doc-C" };
        RagEvaluationMetrics.PrecisionAtK(retrieved, Relevant, 3).Should().Be(1.0);
    }

    [Fact]
    public void PrecisionAtK_HalfRelevant_Returns05()
    {
        var retrieved = new List<string> { "doc-A", "doc-X", "doc-B", "doc-Y" };
        RagEvaluationMetrics.PrecisionAtK(retrieved, Relevant, 4).Should().Be(0.5);
    }

    [Fact]
    public void PrecisionAtK_NoneRelevant_Returns0()
    {
        var retrieved = new List<string> { "doc-X", "doc-Y" };
        RagEvaluationMetrics.PrecisionAtK(retrieved, Relevant, 2).Should().Be(0.0);
    }

    // ── MRR@K ────────────────────────────────────────────────────────

    [Fact]
    public void MrrAtK_FirstIsRelevant_Returns1()
    {
        var retrieved = new List<string> { "doc-A", "doc-X" };
        RagEvaluationMetrics.MrrAtK(retrieved, Relevant, 5).Should().Be(1.0);
    }

    [Fact]
    public void MrrAtK_SecondIsRelevant_ReturnsHalf()
    {
        var retrieved = new List<string> { "doc-X", "doc-B", "doc-Y" };
        RagEvaluationMetrics.MrrAtK(retrieved, Relevant, 5).Should().Be(0.5);
    }

    [Fact]
    public void MrrAtK_ThirdIsRelevant_ReturnsOneThird()
    {
        var retrieved = new List<string> { "doc-X", "doc-Y", "doc-C" };
        RagEvaluationMetrics.MrrAtK(retrieved, Relevant, 5).Should().BeApproximately(1.0 / 3, 0.001);
    }

    [Fact]
    public void MrrAtK_NoneRelevant_Returns0()
    {
        var retrieved = new List<string> { "doc-X", "doc-Y" };
        RagEvaluationMetrics.MrrAtK(retrieved, Relevant, 5).Should().Be(0.0);
    }

    [Fact]
    public void MrrAtK_RelevantBeyondK_Returns0()
    {
        var retrieved = new List<string> { "doc-X", "doc-Y", "doc-A" };
        RagEvaluationMetrics.MrrAtK(retrieved, Relevant, 2).Should().Be(0.0);
    }

    // ── NDCG@K ───────────────────────────────────────────────────────

    [Fact]
    public void NdcgAtK_PerfectRanking_Returns1()
    {
        // All 3 relevant docs at top, which is ideal ranking
        var retrieved = new List<string> { "doc-A", "doc-B", "doc-C", "doc-X" };
        RagEvaluationMetrics.NdcgAtK(retrieved, Relevant, 4).Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void NdcgAtK_ImperfectRanking_LessThan1()
    {
        // Relevant docs pushed down by irrelevant ones
        var retrieved = new List<string> { "doc-X", "doc-A", "doc-Y", "doc-B" };
        var ndcg = RagEvaluationMetrics.NdcgAtK(retrieved, Relevant, 4);
        ndcg.Should().BeLessThan(1.0);
        ndcg.Should().BeGreaterThan(0.0);
    }

    [Fact]
    public void NdcgAtK_NoRelevantDocs_Returns0()
    {
        var retrieved = new List<string> { "doc-X", "doc-Y" };
        RagEvaluationMetrics.NdcgAtK(retrieved, Relevant, 2).Should().Be(0.0);
    }

    [Fact]
    public void NdcgAtK_EmptyRelevantSet_Returns0()
    {
        var retrieved = new List<string> { "doc-A" };
        RagEvaluationMetrics.NdcgAtK(retrieved, new HashSet<string>(), 3).Should().Be(0.0);
    }

    // ── Hit@K ────────────────────────────────────────────────────────

    [Fact]
    public void HitAtK_HasRelevant_Returns1()
    {
        var retrieved = new List<string> { "doc-X", "doc-A" };
        RagEvaluationMetrics.HitAtK(retrieved, Relevant, 5).Should().Be(1.0);
    }

    [Fact]
    public void HitAtK_NoRelevant_Returns0()
    {
        var retrieved = new List<string> { "doc-X", "doc-Y" };
        RagEvaluationMetrics.HitAtK(retrieved, Relevant, 5).Should().Be(0.0);
    }

    // ── Average Precision ────────────────────────────────────────────

    [Fact]
    public void AveragePrecision_PerfectRanking()
    {
        // Perfect: all relevant at top
        var retrieved = new List<string> { "doc-A", "doc-B", "doc-C", "doc-X" };
        var ap = RagEvaluationMetrics.AveragePrecision(retrieved, Relevant, 4);
        // AP = (1/1 + 2/2 + 3/3) / 3 = 1.0
        ap.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void AveragePrecision_MixedRanking()
    {
        var retrieved = new List<string> { "doc-X", "doc-A", "doc-Y", "doc-B" };
        var ap = RagEvaluationMetrics.AveragePrecision(retrieved, Relevant, 4);
        // AP = (1/2 + 2/4) / 3 = (0.5 + 0.5) / 3 = 1/3
        ap.Should().BeApproximately(1.0 / 3, 0.001);
    }

    [Fact]
    public void AveragePrecision_NoRelevant_Returns0()
    {
        var retrieved = new List<string> { "doc-X", "doc-Y" };
        RagEvaluationMetrics.AveragePrecision(retrieved, Relevant, 5).Should().Be(0.0);
    }

    // ── Edge cases ───────────────────────────────────────────────────

    [Fact]
    public void AllMetrics_EmptyRetrievedList_Return0()
    {
        var empty = new List<string>();
        RagEvaluationMetrics.RecallAtK(empty, Relevant, 5).Should().Be(0.0);
        RagEvaluationMetrics.PrecisionAtK(empty, Relevant, 5).Should().Be(0.0);
        RagEvaluationMetrics.MrrAtK(empty, Relevant, 5).Should().Be(0.0);
        RagEvaluationMetrics.NdcgAtK(empty, Relevant, 5).Should().Be(0.0);
        RagEvaluationMetrics.HitAtK(empty, Relevant, 5).Should().Be(0.0);
        RagEvaluationMetrics.AveragePrecision(empty, Relevant, 5).Should().Be(0.0);
    }

    [Fact]
    public void AllMetrics_KEqualsZero_Return0()
    {
        var retrieved = new List<string> { "doc-A" };
        RagEvaluationMetrics.RecallAtK(retrieved, Relevant, 0).Should().Be(0.0);
        RagEvaluationMetrics.PrecisionAtK(retrieved, Relevant, 0).Should().Be(0.0);
        RagEvaluationMetrics.MrrAtK(retrieved, Relevant, 0).Should().Be(0.0);
        RagEvaluationMetrics.NdcgAtK(retrieved, Relevant, 0).Should().Be(0.0);
        RagEvaluationMetrics.HitAtK(retrieved, Relevant, 0).Should().Be(0.0);
    }

    [Fact]
    public void Metrics_DuplicateRetrievedDocs_CountedOnce()
    {
        // Same doc appearing multiple times should count only once
        var retrieved = new List<string> { "doc-A", "doc-A", "doc-A" };
        RagEvaluationMetrics.MrrAtK(retrieved, Relevant, 3).Should().Be(1.0);
    }
}
