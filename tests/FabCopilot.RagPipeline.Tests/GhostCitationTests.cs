using FluentAssertions;
using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

/// <summary>
/// Ghost Citation Prevention 테스트 — 응답에 반영되지 않은 인용 필터링
/// </summary>
public class GhostCitationTests
{
    private static RetrievalResult MakeResult(string chunkText, float score = 0.85f)
        => new()
        {
            DocumentId = "doc-1",
            ChunkText = chunkText,
            Score = score,
            Metadata = new Dictionary<string, object> { ["file_name"] = "test.md" }
        };

    // ──────────────────────────────────────────────────────────────
    // 응답에 포함된 chunk → 유지
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void FilterGhostCitations_RelevantChunk_IsKept()
    {
        var ragResults = new List<RetrievalResult>
        {
            MakeResult("CMP 패드 교체 절차에서 컨디셔너 디스크를 제거하고 새 패드를 장착합니다.")
        };

        var response = "CMP 패드 교체 절차는 다음과 같습니다. 먼저 컨디셔너 디스크를 제거하세요. 그런 다음 새 패드를 장착합니다.";

        var filtered = LlmWorker.FilterGhostCitations(response, ragResults);

        filtered.Should().HaveCount(1);
    }

    // ──────────────────────────────────────────────────────────────
    // 응답에 무관한 chunk → 제거
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void FilterGhostCitations_IrrelevantChunk_IsRemoved()
    {
        var ragResults = new List<RetrievalResult>
        {
            MakeResult("반도체 세정 공정에서 불산(HF) 용액의 농도 관리가 중요합니다. 온도와 유량을 모니터링하세요.")
        };

        var response = "CMP 패드 교체 절차는 다음과 같습니다. 먼저 디스크를 제거하세요.";

        var filtered = LlmWorker.FilterGhostCitations(response, ragResults);

        filtered.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────
    // 혼합: 관련 + 무관한 chunk
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void FilterGhostCitations_MixedChunks_KeepsOnlyRelevant()
    {
        var ragResults = new List<RetrievalResult>
        {
            MakeResult("CMP 패드 교체 절차에서 컨디셔너 디스크를 제거합니다."),
            MakeResult("에칭 공정에서 플라즈마 가스 유량을 조절합니다. RF 파워를 설정하세요.")
        };

        var response = "CMP 패드 교체 절차에 따라 컨디셔너 디스크를 제거하고 새 패드를 장착합니다.";

        var filtered = LlmWorker.FilterGhostCitations(response, ragResults);

        filtered.Should().HaveCount(1);
        filtered[0].ChunkText.Should().Contain("CMP");
    }

    // ──────────────────────────────────────────────────────────────
    // 빈 응답 → 모든 citation 제거
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void FilterGhostCitations_EmptyResponse_ReturnsEmpty()
    {
        var ragResults = new List<RetrievalResult>
        {
            MakeResult("CMP 패드 교체 절차")
        };

        var filtered = LlmWorker.FilterGhostCitations("", ragResults);

        filtered.Should().BeEmpty();
    }

    [Fact]
    public void FilterGhostCitations_NullResponse_ReturnsEmpty()
    {
        var ragResults = new List<RetrievalResult>
        {
            MakeResult("CMP 패드 교체 절차")
        };

        var filtered = LlmWorker.FilterGhostCitations(null!, ragResults);

        filtered.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────
    // RAG 없음 → 빈 리스트
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void FilterGhostCitations_NoRagResults_ReturnsEmpty()
    {
        var filtered = LlmWorker.FilterGhostCitations("Some response text", []);

        filtered.Should().BeEmpty();
    }

    [Fact]
    public void FilterGhostCitations_NullRagResults_ReturnsEmpty()
    {
        var filtered = LlmWorker.FilterGhostCitations("Some response text", null!);

        filtered.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────
    // ExtractKeywords 유닛 테스트
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractKeywords_NormalText_ExtractsWords()
    {
        var keywords = LlmWorker.ExtractKeywords("CMP 패드 교체 절차");

        keywords.Should().Contain("cmp");
        keywords.Should().Contain("패드");
        keywords.Should().Contain("교체");
        keywords.Should().Contain("절차");
    }

    [Fact]
    public void ExtractKeywords_EmptyText_ReturnsEmpty()
    {
        LlmWorker.ExtractKeywords("").Should().BeEmpty();
        LlmWorker.ExtractKeywords(null!).Should().BeEmpty();
    }

    [Fact]
    public void ExtractKeywords_SingleCharWords_Filtered()
    {
        var keywords = LlmWorker.ExtractKeywords("a b c 패드");

        keywords.Should().Contain("패드");
        keywords.Should().NotContain("a");
    }
}
