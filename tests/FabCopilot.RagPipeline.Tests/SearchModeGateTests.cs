using FluentAssertions;
using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

/// <summary>
/// Hybrid vs Strict 검색 모드의 Gate A 임계값 및 신뢰도 판정 검증
/// </summary>
public class SearchModeGateTests
{
    private const float DefaultThreshold = 0.55f;

    private static List<RetrievalResult> MakeRagResults(float score)
        => [new RetrievalResult
        {
            DocumentId = "doc-1",
            ChunkText = "테스트 문서 내용",
            Score = score,
            Metadata = new Dictionary<string, object> { ["file_name"] = "test.md" }
        }];

    // ──────────────────────────────────────────────────────────────
    // ComputeEffectiveThreshold 테스트
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeEffectiveThreshold_Hybrid_ReturnsBaseThreshold()
    {
        var threshold = LlmWorker.ComputeEffectiveThreshold("hybrid", DefaultThreshold);
        threshold.Should().Be(DefaultThreshold);
    }

    [Fact]
    public void ComputeEffectiveThreshold_Strict_ReturnsAtLeast075()
    {
        var threshold = LlmWorker.ComputeEffectiveThreshold("strict", DefaultThreshold);
        threshold.Should().Be(0.75f);
    }

    [Fact]
    public void ComputeEffectiveThreshold_Strict_KeepsHigherBaseThreshold()
    {
        // 기본 임계값이 0.75보다 높으면 그대로 유지
        var threshold = LlmWorker.ComputeEffectiveThreshold("strict", 0.85f);
        threshold.Should().Be(0.85f);
    }

    [Fact]
    public void ComputeEffectiveThreshold_Null_ReturnsBaseThreshold()
    {
        var threshold = LlmWorker.ComputeEffectiveThreshold(null, DefaultThreshold);
        threshold.Should().Be(DefaultThreshold);
    }

    [Fact]
    public void ComputeEffectiveThreshold_Strict_CaseInsensitive()
    {
        LlmWorker.ComputeEffectiveThreshold("STRICT", DefaultThreshold).Should().Be(0.75f);
        LlmWorker.ComputeEffectiveThreshold("Strict", DefaultThreshold).Should().Be(0.75f);
    }

    // ──────────────────────────────────────────────────────────────
    // EvaluateConfidence 테스트
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateConfidence_EmptyResults_AlwaysConfident()
    {
        var result = LlmWorker.EvaluateConfidence([], 0f, 0.75f);
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateConfidence_ScoreAboveThreshold_Confident()
    {
        var results = MakeRagResults(0.80f);
        var result = LlmWorker.EvaluateConfidence(results, 0.80f, 0.75f);
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateConfidence_ScoreEqualToThreshold_Confident()
    {
        var results = MakeRagResults(0.75f);
        var result = LlmWorker.EvaluateConfidence(results, 0.75f, 0.75f);
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateConfidence_ScoreBelowThreshold_NotConfident()
    {
        var results = MakeRagResults(0.60f);
        var result = LlmWorker.EvaluateConfidence(results, 0.60f, 0.75f);
        result.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────
    // Hybrid 모드 통합 시나리오
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.55f, true)]   // 경계값: 정확히 임계값 → 신뢰
    [InlineData(0.60f, true)]   // 임계값 초과 → 신뢰
    [InlineData(0.90f, true)]   // 높은 점수 → 신뢰
    [InlineData(0.54f, false)]  // 임계값 미만 → 비신뢰
    [InlineData(0.30f, false)]  // 낮은 점수 → 비신뢰
    public void Hybrid_ConfidenceDecision(float score, bool expectedConfident)
    {
        var threshold = LlmWorker.ComputeEffectiveThreshold("hybrid", DefaultThreshold);
        var results = MakeRagResults(score);
        var isConfident = LlmWorker.EvaluateConfidence(results, score, threshold);

        isConfident.Should().Be(expectedConfident);
    }

    [Theory]
    [InlineData(0.75f, true)]   // 경계값: 정확히 임계값 → 신뢰
    [InlineData(0.80f, true)]   // 임계값 초과 → 신뢰
    [InlineData(0.74f, false)]  // 임계값 미만 → 비신뢰
    [InlineData(0.60f, false)]  // Hybrid에서는 신뢰, Strict에서는 비신뢰
    [InlineData(0.55f, false)]  // Hybrid에서는 신뢰, Strict에서는 비신뢰
    [InlineData(0.30f, false)]  // 낮은 점수 → 비신뢰
    public void Strict_ConfidenceDecision(float score, bool expectedConfident)
    {
        var threshold = LlmWorker.ComputeEffectiveThreshold("strict", DefaultThreshold);
        var results = MakeRagResults(score);
        var isConfident = LlmWorker.EvaluateConfidence(results, score, threshold);

        isConfident.Should().Be(expectedConfident);
    }

    // ──────────────────────────────────────────────────────────────
    // Hybrid vs Strict 분기점 검증 (0.55~0.75 사이 점수)
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.60f)]
    [InlineData(0.65f)]
    [InlineData(0.70f)]
    public void ScoreBetween055And075_HybridConfident_StrictNotConfident(float score)
    {
        var results = MakeRagResults(score);

        var hybridThreshold = LlmWorker.ComputeEffectiveThreshold("hybrid", DefaultThreshold);
        var strictThreshold = LlmWorker.ComputeEffectiveThreshold("strict", DefaultThreshold);

        var hybridConfident = LlmWorker.EvaluateConfidence(results, score, hybridThreshold);
        var strictConfident = LlmWorker.EvaluateConfidence(results, score, strictThreshold);

        hybridConfident.Should().BeTrue("Hybrid 모드에서 {0} 점수는 0.55 이상이므로 신뢰", score);
        strictConfident.Should().BeFalse("Strict 모드에서 {0} 점수는 0.75 미만이므로 비신뢰", score);
    }

    // ──────────────────────────────────────────────────────────────
    // BuildSystemPrompt 프롬프트 분기 검증
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void Hybrid_Score060_Prompt_UsesMandatoryReference()
    {
        var results = MakeRagResults(0.60f);
        var threshold = LlmWorker.ComputeEffectiveThreshold("hybrid", DefaultThreshold);
        var isConfident = LlmWorker.EvaluateConfidence(results, 0.60f, threshold);

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, results, isConfident);

        prompt.Should().Contain("REFERENCE CONTEXT");
        prompt.Should().NotContain("LOW CONFIDENCE WARNING");
    }

    [Fact]
    public void Strict_Score060_Prompt_ShowsGateAWarning()
    {
        var results = MakeRagResults(0.60f);
        var threshold = LlmWorker.ComputeEffectiveThreshold("strict", DefaultThreshold);
        var isConfident = LlmWorker.EvaluateConfidence(results, 0.60f, threshold);

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, results, isConfident);

        prompt.Should().Contain("LOW CONFIDENCE WARNING");
        prompt.Should().Contain("LOW CONFIDENCE");
        prompt.Should().NotContain("REFERENCE CONTEXT");
    }

    [Fact]
    public void Strict_Score080_Prompt_UsesMandatoryReference()
    {
        var results = MakeRagResults(0.80f);
        var threshold = LlmWorker.ComputeEffectiveThreshold("strict", DefaultThreshold);
        var isConfident = LlmWorker.EvaluateConfidence(results, 0.80f, threshold);

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, results, isConfident);

        prompt.Should().Contain("REFERENCE CONTEXT");
        prompt.Should().NotContain("LOW CONFIDENCE WARNING");
    }

    [Fact]
    public void BothModes_NoResults_NoGateAWarning()
    {
        // RAG 결과 없으면 항상 confident (Gate A 미적용)
        var hybridThreshold = LlmWorker.ComputeEffectiveThreshold("hybrid", DefaultThreshold);
        var strictThreshold = LlmWorker.ComputeEffectiveThreshold("strict", DefaultThreshold);

        LlmWorker.EvaluateConfidence([], 0f, hybridThreshold).Should().BeTrue();
        LlmWorker.EvaluateConfidence([], 0f, strictThreshold).Should().BeTrue();

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, [], isConfident: true);
        prompt.Should().Contain("NO REFERENCE DOCUMENTS AVAILABLE");
        prompt.Should().NotContain("LOW CONFIDENCE WARNING");
    }
}
