using FluentAssertions;
using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

/// <summary>
/// Gate C (응답 품질 검증) 테스트
/// </summary>
public class GateCTests
{
    private static List<RetrievalResult> MakeRagResults(int count = 1)
        => Enumerable.Range(0, count).Select(i => new RetrievalResult
        {
            DocumentId = $"doc-{i + 1}",
            ChunkText = "CMP 패드 교체 절차에 대한 상세 설명입니다. 컨디셔너 디스크를 제거하세요.",
            Score = 0.85f,
            Metadata = new Dictionary<string, object> { ["file_name"] = "test.md" }
        }).ToList();

    // ──────────────────────────────────────────────────────────────
    // 짧은 응답 → 경고
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateGateC_ShortResponse_WarnsMinLength()
    {
        var result = LlmWorker.EvaluateGateC("짧은 답변", []);

        result.Passed.Should().BeFalse();
        result.Warnings.Should().Contain(w => w.Contains("50자 미만"));
    }

    // ──────────────────────────────────────────────────────────────
    // 반복 텍스트 → 경고
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateGateC_RepeatedWords_WarnsRepetition()
    {
        var repeatedText = "확인하세요 확인하세요 확인하세요 확인하세요 이 내용은 충분히 길어야 합니다. 최소 50자 이상의 텍스트가 필요합니다.";
        var result = LlmWorker.EvaluateGateC(repeatedText, []);

        result.Passed.Should().BeFalse();
        result.Warnings.Should().Contain(w => w.Contains("반복"));
    }

    [Fact]
    public void EvaluateGateC_RepeatedSentences_WarnsRepetition()
    {
        var repeatedText = "이 문장은 반복됩니다. 이 문장은 반복됩니다. 이 문장은 반복됩니다. 충분히 긴 응답 텍스트를 작성합니다.";
        var result = LlmWorker.EvaluateGateC(repeatedText, []);

        result.Passed.Should().BeFalse();
        result.Warnings.Should().Contain(w => w.Contains("반복"));
    }

    // ──────────────────────────────────────────────────────────────
    // 한자 포함 → 경고
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateGateC_ContainsChinese_WarnsLanguageViolation()
    {
        var chineseText = "이 문제는 設備의 故障으로 인한 것입니다. 충분히 긴 응답을 작성하여 최소 길이 조건을 만족합니다.";
        var result = LlmWorker.EvaluateGateC(chineseText, []);

        result.Passed.Should().BeFalse();
        result.Warnings.Should().Contain(w => w.Contains("한자"));
    }

    // ──────────────────────────────────────────────────────────────
    // RAG 있어도 citation 체크 안 함 (서비스가 자동 처리)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateGateC_RagPresentButNoCitation_NoStructureWarning()
    {
        // Citation section check removed — service generates citations post-LLM
        var ragResults = MakeRagResults();
        var textWithoutRef = "이 답변은 패드 교체에 대한 설명입니다. 디스크를 제거하고 새 패드를 장착하세요. 추가 정보가 필요하면 문의하세요.";

        var result = LlmWorker.EvaluateGateC(textWithoutRef, ragResults);

        result.Warnings.Should().NotContain(w => w.Contains("참조 섹션"));
    }

    // ──────────────────────────────────────────────────────────────
    // 빈 응답 → 경고
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateGateC_EmptyResponse_WarnsEmpty()
    {
        var result = LlmWorker.EvaluateGateC("", []);

        result.Passed.Should().BeFalse();
        result.Warnings.Should().Contain(w => w.Contains("비어 있"));
    }

    [Fact]
    public void EvaluateGateC_WhitespaceOnly_WarnsEmpty()
    {
        var result = LlmWorker.EvaluateGateC("   \n\t  ", []);

        result.Passed.Should().BeFalse();
        result.Warnings.Should().Contain(w => w.Contains("비어 있"));
    }

    [Fact]
    public void EvaluateGateC_SpecialCharsOnly_WarnsEmpty()
    {
        var result = LlmWorker.EvaluateGateC("---***---", []);

        result.Passed.Should().BeFalse();
        result.Warnings.Should().Contain(w => w.Contains("비어 있"));
    }

    // ──────────────────────────────────────────────────────────────
    // 정상 응답 → 통과
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateGateC_NormalResponse_Passes()
    {
        var normalText = """
            ## 요약
            CMP 장비의 패드 교체는 정기적으로 수행해야 합니다.

            ## 상세
            1. 기존 패드를 제거합니다.
            2. 새 패드를 장착합니다.
            3. 컨디셔닝을 수행합니다.

            ## 참조
            📄 pad-replacement-guide.md
            """;

        var ragResults = MakeRagResults();
        var result = LlmWorker.EvaluateGateC(normalText, ragResults);

        result.Passed.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void EvaluateGateC_NormalResponseNoRag_Passes()
    {
        var normalText = "CMP 장비는 반도체 제조 공정에서 웨이퍼의 표면을 평탄화하는 데 사용되는 장비입니다. Chemical Mechanical Planarization의 약자입니다.";

        var result = LlmWorker.EvaluateGateC(normalText, []);

        result.Passed.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────
    // HasExcessiveRepetition 유닛 테스트
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void HasExcessiveRepetition_NoRepetition_ReturnsFalse()
    {
        LlmWorker.HasExcessiveRepetition("이것은 정상적인 텍스트입니다.").Should().BeFalse();
    }

    [Fact]
    public void HasExcessiveRepetition_ThreeConsecutiveWords_ReturnsTrue()
    {
        LlmWorker.HasExcessiveRepetition("확인 확인 확인 다음 단계").Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────
    // ContainsChineseCharacters 유닛 테스트
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ContainsChineseCharacters_PureKorean_ReturnsFalse()
    {
        LlmWorker.ContainsChineseCharacters("한국어 텍스트입니다").Should().BeFalse();
    }

    [Fact]
    public void ContainsChineseCharacters_WithChinese_ReturnsTrue()
    {
        LlmWorker.ContainsChineseCharacters("이것은 漢字 포함 텍스트").Should().BeTrue();
    }

    [Fact]
    public void ContainsChineseCharacters_EnglishOnly_ReturnsFalse()
    {
        LlmWorker.ContainsChineseCharacters("This is English text").Should().BeFalse();
    }
}
