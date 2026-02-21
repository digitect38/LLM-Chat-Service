using FabCopilot.Contracts.Enums;
using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class QueryRouterClassifyTests
{
    // ─── Error Intent ─────────────────────────────────────────────

    [Theory]
    [InlineData("A123 알람이 발생했습니다")]
    [InlineData("에러 코드 E001 해결 방법")]
    [InlineData("alarm code A15 원인")]
    [InlineData("오류가 발생합니다")]
    [InlineData("경고 메시지가 표시됩니다")]
    [InlineData("Error occurred during process")]
    [InlineData("fault detected in chamber")]
    public void Classify_ErrorKeywords_ReturnsError(string query)
    {
        QueryRouter.Classify(query).Should().Be(QueryIntent.Error);
    }

    // ─── Procedure Intent ─────────────────────────────────────────

    [Theory]
    [InlineData("패드 교체 절차")]
    [InlineData("컨디셔너 교체 방법")]
    [InlineData("슬러리 공급 조치")]
    [InlineData("설치 순서 알려주세요")]
    [InlineData("how to replace the pad")]
    [InlineData("installation steps")]
    public void Classify_ProcedureKeywords_ReturnsProcedure(string query)
    {
        QueryRouter.Classify(query).Should().Be(QueryIntent.Procedure);
    }

    // ─── Part Intent ──────────────────────────────────────────────

    [Theory]
    [InlineData("패드 수명은 얼마나 됩니까")]
    [InlineData("소모품 교환 주기")]
    [InlineData("리테이너 링 부품 번호")]
    [InlineData("consumable lifetime")]
    [InlineData("컨디셔너 디스크 부품")]
    public void Classify_PartKeywords_ReturnsPart(string query)
    {
        QueryRouter.Classify(query).Should().Be(QueryIntent.Part);
    }

    // ─── Definition Intent ────────────────────────────────────────

    [Theory]
    [InlineData("CMP란 무엇인가요")]
    [InlineData("다운포스의 정의")]
    [InlineData("슬러리 의미가 뭔가요")]
    [InlineData("what is CMP")]
    [InlineData("define polishing rate")]
    public void Classify_DefinitionKeywords_ReturnsDefinition(string query)
    {
        QueryRouter.Classify(query).Should().Be(QueryIntent.Definition);
    }

    // ─── Spec Intent ──────────────────────────────────────────────

    [Theory]
    [InlineData("다운포스 설정값")]
    [InlineData("회전속도 파라미터 범위")]
    [InlineData("사양 확인")]
    [InlineData("specification range")]
    [InlineData("threshold parameter")]
    public void Classify_SpecKeywords_ReturnsSpec(string query)
    {
        QueryRouter.Classify(query).Should().Be(QueryIntent.Spec);
    }

    // ─── Comparison Intent ────────────────────────────────────────

    [Theory]
    [InlineData("IC1000과 IC1010 비교")]
    [InlineData("oxide vs metal 차이")]
    [InlineData("두 모델의 다른점")]
    [InlineData("compare slurry types")]
    public void Classify_ComparisonKeywords_ReturnsComparison(string query)
    {
        QueryRouter.Classify(query).Should().Be(QueryIntent.Comparison);
    }

    // ─── General Intent ───────────────────────────────────────────

    [Theory]
    [InlineData("CMP 공정 개요")]
    [InlineData("안녕하세요")]
    [InlineData("tell me about the system")]
    [InlineData("")]
    public void Classify_NoKeywordMatch_ReturnsGeneral(string query)
    {
        QueryRouter.Classify(query).Should().Be(QueryIntent.General);
    }

    // ─── Preferred Doc Types ──────────────────────────────────────

    [Fact]
    public void GetPreferredDocTypes_Error_ReturnsAlarmDocs()
    {
        var types = QueryRouter.GetPreferredDocTypes(QueryIntent.Error);
        types.Should().Contain("alarm");
        types.Should().Contain("troubleshooting");
    }

    [Fact]
    public void GetPreferredDocTypes_General_ReturnsNull()
    {
        var types = QueryRouter.GetPreferredDocTypes(QueryIntent.General);
        types.Should().BeNull();
    }

    [Fact]
    public void GetPreferredDocTypes_Procedure_ReturnsProcedureDocs()
    {
        var types = QueryRouter.GetPreferredDocTypes(QueryIntent.Procedure);
        types.Should().Contain("procedure");
        types.Should().Contain("maintenance");
    }
}
