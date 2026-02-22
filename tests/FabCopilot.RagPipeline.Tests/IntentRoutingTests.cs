using FluentAssertions;
using FabCopilot.Contracts.Enums;
using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

/// <summary>
/// Intent 기반 LLM 프롬프트 라우팅 테스트
/// </summary>
public class IntentRoutingTests
{
    private static List<RetrievalResult> MakeRagResults()
        => [new RetrievalResult
        {
            DocumentId = "doc-1",
            ChunkText = "테스트 문서 내용입니다.",
            Score = 0.85f,
            Metadata = new Dictionary<string, object> { ["file_name"] = "test.md" }
        }];

    // ──────────────────────────────────────────────────────────────
    // BuildIntentStyleSection 직접 테스트
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void BuildIntentStyleSection_Error_ContainsAlarmStyle()
    {
        var style = LlmWorker.BuildIntentStyleSection(QueryIntent.Error);

        style.Should().Contain("RESPONSE STYLE - ERROR/ALARM");
        style.Should().Contain("알람 코드");
        style.Should().Contain("조치 방법");
    }

    [Fact]
    public void BuildIntentStyleSection_Procedure_ContainsProcedureStyle()
    {
        var style = LlmWorker.BuildIntentStyleSection(QueryIntent.Procedure);

        style.Should().Contain("RESPONSE STYLE - PROCEDURE");
        style.Should().Contain("단계별 절차");
    }

    [Fact]
    public void BuildIntentStyleSection_Part_ContainsPartStyle()
    {
        var style = LlmWorker.BuildIntentStyleSection(QueryIntent.Part);

        style.Should().Contain("RESPONSE STYLE - PART/CONSUMABLE");
        style.Should().Contain("부품명");
    }

    [Fact]
    public void BuildIntentStyleSection_Definition_ContainsDefinitionStyle()
    {
        var style = LlmWorker.BuildIntentStyleSection(QueryIntent.Definition);

        style.Should().Contain("RESPONSE STYLE - DEFINITION");
        style.Should().Contain("정의");
        style.Should().Contain("관련 용어");
    }

    [Fact]
    public void BuildIntentStyleSection_Spec_ContainsSpecStyle()
    {
        var style = LlmWorker.BuildIntentStyleSection(QueryIntent.Spec);

        style.Should().Contain("RESPONSE STYLE - SPECIFICATION");
        style.Should().Contain("파라미터");
    }

    [Fact]
    public void BuildIntentStyleSection_Comparison_ContainsComparisonStyle()
    {
        var style = LlmWorker.BuildIntentStyleSection(QueryIntent.Comparison);

        style.Should().Contain("RESPONSE STYLE - COMPARISON");
        style.Should().Contain("비교 항목");
    }

    [Fact]
    public void BuildIntentStyleSection_General_ReturnsEmpty()
    {
        var style = LlmWorker.BuildIntentStyleSection(QueryIntent.General);

        style.Should().BeEmpty();
    }

    [Fact]
    public void BuildIntentStyleSection_Null_ReturnsEmpty()
    {
        var style = LlmWorker.BuildIntentStyleSection(null);

        style.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────
    // BuildSystemPrompt에 Intent 스타일이 통합되는지 검증
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_WithErrorIntent_IncludesAlarmStyle()
    {
        var ragResults = MakeRagResults();

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults, isConfident: true, QueryIntent.Error);

        prompt.Should().Contain("RESPONSE STYLE - ERROR/ALARM");
        prompt.Should().Contain("REFERENCE DOCUMENTS - MANDATORY USE");
    }

    [Fact]
    public void BuildSystemPrompt_WithProcedureIntent_IncludesProcedureStyle()
    {
        var ragResults = MakeRagResults();

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults, isConfident: true, QueryIntent.Procedure);

        prompt.Should().Contain("RESPONSE STYLE - PROCEDURE");
    }

    [Fact]
    public void BuildSystemPrompt_WithGeneralIntent_NoStyleSection()
    {
        var ragResults = MakeRagResults();

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults, isConfident: true, QueryIntent.General);

        prompt.Should().NotContain("RESPONSE STYLE");
    }

    [Fact]
    public void BuildSystemPrompt_WithNullIntent_NoStyleSection()
    {
        var ragResults = MakeRagResults();

        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, ragResults, isConfident: true, null);

        prompt.Should().NotContain("RESPONSE STYLE");
    }

    [Fact]
    public void BuildSystemPrompt_NoRag_WithIntent_StillIncludesStyle()
    {
        var prompt = LlmWorker.BuildSystemPrompt("CMP-001", null, [], isConfident: true, QueryIntent.Definition);

        prompt.Should().Contain("RESPONSE STYLE - DEFINITION");
        prompt.Should().Contain("NO REFERENCE DOCUMENTS AVAILABLE");
    }
}
