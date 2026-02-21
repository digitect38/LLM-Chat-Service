using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

/// <summary>
/// Tests for DocumentIngestor.FlattenMarkdownTables — converts markdown tables
/// to natural language before embedding to prevent content dilution.
/// </summary>
public class FlattenMarkdownTablesTests
{
    // === Basic Table Conversion ===

    [Fact]
    public void FlattenMarkdownTables_PadReplacementCriteria_ProducesNaturalLanguage()
    {
        var input = """
            [# CMP 슬러리 및 패드 교체 절차 > ## 1. 패드 교체 절차 > ### 1.1 교체 판단 기준]
            패드 교체 판단 기준이다.

            | 기준 | 임계값 | 측정 방법 |
            |------|--------|----------|
            | 사용 시간 | > 500시간 | 장비 로그 |
            | 패드 두께 | < 1.0 mm | 두께 게이지 |
            """;

        var result = DocumentIngestor.FlattenMarkdownTables(input);

        // Table pipes and separator should be gone
        result.Should().NotContain("|---");
        // Content should be converted to natural language
        result.Should().Contain("기준: 사용 시간");
        result.Should().Contain("임계값: > 500시간");
        result.Should().Contain("측정 방법: 장비 로그");
        result.Should().Contain("기준: 패드 두께");
        // Header prefix should be preserved
        result.Should().Contain("[# CMP 슬러리");
    }

    [Fact]
    public void FlattenMarkdownTables_SlurryReplacementCriteria_PreservesAllRows()
    {
        var input = """
            | 기준 | 임계값 | 측정 방법 |
            |------|--------|----------|
            | 잔량 | < 10% | 탱크 레벨 센서 |
            | 사용 기간 | > 72시간(개봉 후) | 개봉 일시 라벨 |
            | pH 변화 | 초기값 대비 ±0.5 | pH meter |
            | Particle size | D50 기준 ±20% | Particle analyzer |
            | MRR 저하 | > 10% | Test wafer |
            """;

        var result = DocumentIngestor.FlattenMarkdownTables(input);

        result.Should().Contain("기준: 잔량");
        result.Should().Contain("기준: 사용 기간");
        result.Should().Contain("기준: pH 변화");
        result.Should().Contain("기준: Particle size");
        result.Should().Contain("기준: MRR 저하");
        result.Should().Contain("임계값: > 10%");
    }

    // === No Table (Passthrough) ===

    [Fact]
    public void FlattenMarkdownTables_PlainText_ReturnsUnchanged()
    {
        var input = "패드 교체 판단 기준이다. 사용 시간이 500시간을 초과하면 교체한다.";
        var result = DocumentIngestor.FlattenMarkdownTables(input);
        result.Should().Be(input);
    }

    [Fact]
    public void FlattenMarkdownTables_Empty_ReturnsEmpty()
    {
        DocumentIngestor.FlattenMarkdownTables("").Should().BeEmpty();
        DocumentIngestor.FlattenMarkdownTables(null!).Should().BeNullOrEmpty();
    }

    // === Mixed Content ===

    [Fact]
    public void FlattenMarkdownTables_MixedTextAndTable_PreservesTextFlattenTable()
    {
        var input = """
            패드 교체 판단 기준이다.

            | 기준 | 임계값 |
            |------|--------|
            | 사용 시간 | > 500시간 |

            Break-in 절차를 진행한다.
            """;

        var result = DocumentIngestor.FlattenMarkdownTables(input);

        // Text before and after should be preserved
        result.Should().Contain("패드 교체 판단 기준이다.");
        result.Should().Contain("Break-in 절차를 진행한다.");
        // Table should be flattened
        result.Should().Contain("기준: 사용 시간");
        result.Should().Contain("임계값: > 500시간");
    }

    [Fact]
    public void FlattenMarkdownTables_MultipleTablesInOneChunk_FlattensAll()
    {
        var input = """
            첫 번째 표:

            | 항목 | 값 |
            |------|-----|
            | A | 100 |

            두 번째 표:

            | 항목 | 값 |
            |------|-----|
            | B | 200 |
            """;

        var result = DocumentIngestor.FlattenMarkdownTables(input);

        result.Should().Contain("항목: A, 값: 100.");
        result.Should().Contain("항목: B, 값: 200.");
    }

    // === Edge Cases ===

    [Fact]
    public void FlattenMarkdownTables_EmptyCells_SkipsEmptyCells()
    {
        var input = """
            | 기준 | 값 | 비고 |
            |------|-----|------|
            | 시간 | 500 |  |
            """;

        var result = DocumentIngestor.FlattenMarkdownTables(input);

        result.Should().Contain("기준: 시간");
        result.Should().Contain("값: 500");
        // Empty "비고" cell should be skipped
        result.Should().NotContain("비고:");
    }

    [Fact]
    public void FlattenMarkdownTables_AlignmentSeparators_HandlesCorrectly()
    {
        // Tables with alignment markers (:---:, ---:, :---)
        var input = """
            | Left | Center | Right |
            |:-----|:------:|------:|
            | L1 | C1 | R1 |
            """;

        var result = DocumentIngestor.FlattenMarkdownTables(input);

        result.Should().Contain("Left: L1");
        result.Should().Contain("Center: C1");
        result.Should().Contain("Right: R1");
    }

    [Fact]
    public void FlattenMarkdownTables_SinglePipeInText_NotTreatedAsTable()
    {
        var input = "범위: 15~25°C | pH: 6~8 범위";
        var result = DocumentIngestor.FlattenMarkdownTables(input);
        result.Should().Be(input);
    }

    // === Real Document Chunks ===

    [Fact]
    public void FlattenMarkdownTables_ActualChunk0_FlattensCorrectly()
    {
        // This is the actual chunk 0 from cmp-slurry-pad-replacement.md that
        // caused the retrieval failure (score 0.5489 vs 0.8043 for wrong docs)
        var input = """
            [# CMP 슬러리 및 패드 교체 절차 > ## 1. 패드 교체 절차 (SOP-CMP-PAD-001) > ### 1.1 교체 판단 기준]
            패드 교체 판단 기준이다.

            | 기준 | 임계값 | 측정 방법 |
            |------|--------|----------|
            | 사용 시간 | > 500시간 | 장비 로그 |
            | 패드 두께 | < 1.0 mm | 두께 게이지 |
            | MRR 저하 | > 15%(신품 대비) | Test wafer |
            | WIWNU 악화 | > 5% | Test wafer |
            | 표면 상태 | glazing, groove 마모 | 육안 검사 |
            """;

        var result = DocumentIngestor.FlattenMarkdownTables(input);

        // All 5 criteria should be preserved as natural language
        result.Should().Contain("기준: 사용 시간, 임계값: > 500시간, 측정 방법: 장비 로그.");
        result.Should().Contain("기준: 패드 두께, 임계값: < 1.0 mm, 측정 방법: 두께 게이지.");
        result.Should().Contain("기준: MRR 저하, 임계값: > 15%(신품 대비), 측정 방법: Test wafer.");
        result.Should().Contain("기준: WIWNU 악화, 임계값: > 5%, 측정 방법: Test wafer.");
        result.Should().Contain("기준: 표면 상태, 임계값: glazing, groove 마모, 측정 방법: 육안 검사.");
    }
}
