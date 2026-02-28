using FabCopilot.McpLogServer.Analysis;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Analysis;

/// <summary>
/// Tests for Tier 3 Auto-Extraction: CausalKnowledgeExtractor.
/// </summary>
public class CausalKnowledgeExtractorTests
{
    private const string DocId = "cmp-troubleshooting.md";
    private const string EquipType = "CMP";

    // ── Table-Based Extraction ───────────────────────────────────────

    [Fact]
    public void ExtractFromChunk_TableRow_ExtractsAllFields()
    {
        var text = """
            | E-1023 | Platen vibration high | Worn conditioner disc | Replace conditioner disc |
            | A-201  | Slurry flow low       | Clogged filter        | Clean or replace filter  |
            """;

        var results = CausalKnowledgeExtractor.ExtractFromChunk(text, DocId, "Ch3", EquipType);

        results.Should().HaveCount(2);
        results[0].ErrorCode.Should().Be("E-1023");
        results[0].Symptom.Should().Be("Platen vibration high");
        results[0].Cause.Should().Be("Worn conditioner disc");
        results[0].Action.Should().Be("Replace conditioner disc");
        results[0].Confidence.Should().Be(0.9);
    }

    [Fact]
    public void ExtractFromChunk_TableRow_SkipsDashCause()
    {
        var text = "| A-100 | Normal operation | - | - |";

        var results = CausalKnowledgeExtractor.ExtractFromChunk(text, DocId);
        results.Should().BeEmpty();
    }

    // ── Structured Block Extraction ──────────────────────────────────

    [Fact]
    public void ExtractFromChunk_KoreanCauseAction_ExtractsCorrectly()
    {
        var text = """
            에러 코드: E-2045
            증상: 슬러리 유량이 정상 범위 이하로 감소
            원인: 슬러리 필터 막힘
            조치: 필터 교체 또는 세척
            """;

        var results = CausalKnowledgeExtractor.ExtractFromChunk(text, DocId, "진단 가이드", EquipType);

        results.Should().HaveCountGreaterOrEqualTo(1);
        var entry = results.First(e => e.ErrorCode == "E-2045");
        entry.Cause.Should().Contain("슬러리 필터");
        entry.SourceSection.Should().Be("진단 가이드");
    }

    [Fact]
    public void ExtractFromChunk_EnglishCauseAction_ExtractsCorrectly()
    {
        var text = """
            Error Code: A305
            Symptom: Temperature sensor deviation
            Root Cause: Faulty thermocouple
            Corrective Action: Replace thermocouple and recalibrate
            """;

        var results = CausalKnowledgeExtractor.ExtractFromChunk(text, DocId);

        results.Should().HaveCountGreaterOrEqualTo(1);
        var entry = results.First(e => e.ErrorCode == "A305");
        entry.Cause.Should().Contain("thermocouple");
    }

    // ── Conditional Pattern Extraction ────────────────────────────────

    [Fact]
    public void ExtractFromChunk_ConditionalPattern_Extracts()
    {
        var text = "If platen vibration exceeds threshold, check conditioner disc condition.";

        var results = CausalKnowledgeExtractor.ExtractFromChunk(text, DocId);

        results.Should().HaveCountGreaterOrEqualTo(1);
        results.Should().Contain(e => e.Action.Contains("conditioner disc"));
    }

    [Fact]
    public void ExtractFromChunk_KoreanConditional_Extracts()
    {
        // Pattern: "발생 시 <symptom>, 점검 <action>"
        var text = "발생 시 슬러리 유량 저하, 점검 필터 상태를 수행하세요.";

        var results = CausalKnowledgeExtractor.ExtractFromChunk(text, DocId);

        results.Should().HaveCountGreaterOrEqualTo(1);
    }

    // ── Deduplication ────────────────────────────────────────────────

    [Fact]
    public void ExtractFromChunk_DuplicateErrorCode_KeepsHighestConfidence()
    {
        var text = """
            | E-1023 | Vibration | Worn disc | Replace disc |
            원인: Worn disc
            """;

        var results = CausalKnowledgeExtractor.ExtractFromChunk(text, DocId);

        // Table extraction (0.9 confidence) should be preferred over structured block (0.7)
        var e1023 = results.Where(r => r.ErrorCode == "E-1023").ToList();
        e1023.Should().HaveCount(1);
        e1023[0].Confidence.Should().Be(0.9);
    }

    // ── RUL Estimate Extraction ──────────────────────────────────────

    [Fact]
    public void ExtractRulEstimates_Korean_Hours()
    {
        var text = "패드 수명: 약 500시간";

        var results = CausalKnowledgeExtractor.ExtractRulEstimates(text, DocId, equipmentType: EquipType);

        results.Should().HaveCount(1);
        results[0].ExpectedLifeHours.Should().Be(500);
        results[0].ComponentName.Should().Contain("패드");
    }

    [Fact]
    public void ExtractRulEstimates_English_Hours()
    {
        var text = "Conditioner disc expected life: 2000 hours";

        var results = CausalKnowledgeExtractor.ExtractRulEstimates(text, DocId);

        results.Should().HaveCount(1);
        results[0].ExpectedLifeHours.Should().Be(2000);
    }

    [Fact]
    public void ExtractRulEstimates_Days_ConvertsToHours()
    {
        var text = "Filter service life: 30 days";

        var results = CausalKnowledgeExtractor.ExtractRulEstimates(text, DocId);

        results.Should().HaveCount(1);
        results[0].ExpectedLifeHours.Should().Be(30 * 24);
    }

    [Fact]
    public void ExtractRulEstimates_Wafers()
    {
        var text = "패드 사용 한도: 5000매 가공 후 교체";

        var results = CausalKnowledgeExtractor.ExtractRulEstimates(text, DocId);

        results.Should().HaveCount(1);
        results[0].ExpectedLifeWafers.Should().Be(5000);
    }

    // ── Edge Cases ───────────────────────────────────────────────────

    [Fact]
    public void ExtractFromChunk_EmptyText_ReturnsEmpty()
    {
        CausalKnowledgeExtractor.ExtractFromChunk("", DocId).Should().BeEmpty();
    }

    [Fact]
    public void ExtractRulEstimates_NoLifespan_ReturnsEmpty()
    {
        CausalKnowledgeExtractor.ExtractRulEstimates("Normal operation text.", DocId)
            .Should().BeEmpty();
    }

    [Fact]
    public void ExtractFromChunk_SetsSourceDocument()
    {
        var text = "| E-100 | test | test cause | test action |";
        var results = CausalKnowledgeExtractor.ExtractFromChunk(text, "my-doc.md", "Section 1", "CMP");

        results.Should().HaveCount(1);
        results[0].SourceDocument.Should().Be("my-doc.md");
        results[0].SourceSection.Should().Be("Section 1");
        results[0].EquipmentType.Should().Be("CMP");
    }
}
