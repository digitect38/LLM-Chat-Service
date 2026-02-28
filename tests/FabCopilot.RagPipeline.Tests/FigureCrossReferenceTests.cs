using FabCopilot.RagService.Services.ImageOcr;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

/// <summary>
/// Tests for figure/table cross-reference detection and caption enrichment.
/// </summary>
public class FigureCrossReferenceTests
{
    // ── Korean figure references ─────────────────────────────────────

    [Fact]
    public void FindReferences_KoreanFigure_Detected()
    {
        var refs = FigureCrossReferenceParser.FindReferences("그림 3.2 참조");
        refs.Should().HaveCount(1);
        refs[0].ReferenceType.Should().Be("figure");
        refs[0].ReferenceId.Should().Be("3.2");
    }

    [Fact]
    public void FindReferences_KoreanDiagram_Detected()
    {
        var refs = FigureCrossReferenceParser.FindReferences("도면 5 참고");
        refs.Should().HaveCount(1);
        refs[0].ReferenceType.Should().Be("diagram");
        refs[0].ReferenceId.Should().Be("5");
    }

    [Fact]
    public void FindReferences_KoreanTable_Detected()
    {
        var refs = FigureCrossReferenceParser.FindReferences("표 2.1에 나와있는 값");
        refs.Should().HaveCount(1);
        refs[0].ReferenceType.Should().Be("table");
        refs[0].ReferenceId.Should().Be("2.1");
    }

    [Fact]
    public void FindReferences_KoreanPhoto_Detected()
    {
        var refs = FigureCrossReferenceParser.FindReferences("사진 1 참조");
        refs.Should().HaveCount(1);
        refs[0].ReferenceType.Should().Be("photo");
        refs[0].ReferenceId.Should().Be("1");
    }

    // ── English figure references ────────────────────────────────────

    [Fact]
    public void FindReferences_EnglishFigure_Detected()
    {
        var refs = FigureCrossReferenceParser.FindReferences("See Figure 4.1 for details");
        refs.Should().HaveCount(1);
        refs[0].ReferenceType.Should().Be("figure");
        refs[0].ReferenceId.Should().Be("4.1");
    }

    [Fact]
    public void FindReferences_EnglishFigAbbreviation_Detected()
    {
        var refs = FigureCrossReferenceParser.FindReferences("As shown in Fig. 7");
        refs.Should().HaveCount(1);
        refs[0].ReferenceType.Should().Be("figure");
        refs[0].ReferenceId.Should().Be("7");
    }

    [Fact]
    public void FindReferences_EnglishTable_Detected()
    {
        var refs = FigureCrossReferenceParser.FindReferences("Refer to Table 3.2");
        refs.Should().HaveCount(1);
        refs[0].ReferenceType.Should().Be("table");
        refs[0].ReferenceId.Should().Be("3.2");
    }

    [Fact]
    public void FindReferences_EnglishDiagram_Detected()
    {
        var refs = FigureCrossReferenceParser.FindReferences("Diagram 2 shows the layout");
        refs.Should().HaveCount(1);
        refs[0].ReferenceType.Should().Be("diagram");
        refs[0].ReferenceId.Should().Be("2");
    }

    // ── Multiple references ──────────────────────────────────────────

    [Fact]
    public void FindReferences_MultipleRefs_AllDetected()
    {
        var text = "그림 1.1을 참고하고, 표 2.3의 값을 확인하세요. Figure 5.2 also applies.";
        var refs = FigureCrossReferenceParser.FindReferences(text);
        refs.Should().HaveCountGreaterOrEqualTo(3);
    }

    [Fact]
    public void FindReferences_EmptyText_ReturnsEmpty()
    {
        FigureCrossReferenceParser.FindReferences("").Should().BeEmpty();
        FigureCrossReferenceParser.FindReferences(null!).Should().BeEmpty();
    }

    [Fact]
    public void FindReferences_NoFigures_ReturnsEmpty()
    {
        var refs = FigureCrossReferenceParser.FindReferences("패드 교체 판단 기준은 500시간입니다.");
        refs.Should().BeEmpty();
    }

    // ── Captions ─────────────────────────────────────────────────────

    [Fact]
    public void FindCaptions_KoreanCaption_Detected()
    {
        var captions = FigureCrossReferenceParser.FindCaptions("그림 3.2: CMP 장비 단면도");
        captions.Should().HaveCount(1);
        captions[0].ReferenceType.Should().Be("figure");
        captions[0].ReferenceId.Should().Be("3.2");
        captions[0].Caption.Should().Be("CMP 장비 단면도");
    }

    [Fact]
    public void FindCaptions_EnglishCaption_Detected()
    {
        var captions = FigureCrossReferenceParser.FindCaptions("Figure 5.1 - Cross-section of polishing head");
        captions.Should().HaveCount(1);
        captions[0].ReferenceType.Should().Be("figure");
        captions[0].ReferenceId.Should().Be("5.1");
        captions[0].Caption.Should().Be("Cross-section of polishing head");
    }

    [Fact]
    public void FindCaptions_TableCaption_Detected()
    {
        var captions = FigureCrossReferenceParser.FindCaptions("표 2.1: 소모품 교체 주기");
        captions.Should().HaveCount(1);
        captions[0].ReferenceType.Should().Be("table");
        captions[0].ReferenceId.Should().Be("2.1");
        captions[0].Caption.Should().Be("소모품 교체 주기");
    }

    [Fact]
    public void FindCaptions_EmptyText_ReturnsEmpty()
    {
        FigureCrossReferenceParser.FindCaptions("").Should().BeEmpty();
    }

    // ── Caption map ──────────────────────────────────────────────────

    [Fact]
    public void BuildCaptionMap_BuildsFromDocument()
    {
        var docText = @"
## 장비 구조

그림 1.1: CMP 장비 전체 구조
그림 1.2: 연마 헤드 상세도
표 1.1: 주요 부품 목록

패드와 슬러리를 사용하여 연마합니다.
그림 1.1 참조.
";
        var map = FigureCrossReferenceParser.BuildCaptionMap(docText);
        map.Should().ContainKey("figure:1.1");
        map["figure:1.1"].Should().Be("CMP 장비 전체 구조");
        map.Should().ContainKey("figure:1.2");
        map.Should().ContainKey("table:1.1");
    }

    // ── Enrichment ───────────────────────────────────────────────────

    [Fact]
    public void EnrichChunkWithFigureContext_AddsCaption()
    {
        var chunk = "패드 구조는 그림 1.1 참조";
        var captionMap = new Dictionary<string, string>
        {
            ["figure:1.1"] = "CMP 연마 패드 단면도"
        };

        var enriched = FigureCrossReferenceParser.EnrichChunkWithFigureContext(chunk, captionMap);
        enriched.Should().Contain("[FIGURE 1.1: CMP 연마 패드 단면도]");
    }

    [Fact]
    public void EnrichChunkWithFigureContext_NoMatch_ReturnsOriginal()
    {
        var chunk = "그림 2.5를 참고하세요";
        var captionMap = new Dictionary<string, string>
        {
            ["figure:1.1"] = "CMP 연마 패드 단면도"
        };

        var enriched = FigureCrossReferenceParser.EnrichChunkWithFigureContext(chunk, captionMap);
        // No match for figure:2.5 in map, so no enrichment
        enriched.Should().NotContain("[FIGURE");
    }

    [Fact]
    public void EnrichChunkWithFigureContext_EmptyMap_ReturnsOriginal()
    {
        var chunk = "패드 교체 절차";
        var enriched = FigureCrossReferenceParser.EnrichChunkWithFigureContext(chunk, new Dictionary<string, string>());
        enriched.Should().Be(chunk);
    }

    // ── Hyphenated IDs ───────────────────────────────────────────────

    [Fact]
    public void FindReferences_HyphenatedId_Detected()
    {
        var refs = FigureCrossReferenceParser.FindReferences("See Figure 3-1 for details");
        refs.Should().HaveCount(1);
        refs[0].ReferenceId.Should().Be("3-1");
    }

    [Fact]
    public void FindReferences_MultiLevelId_Detected()
    {
        var refs = FigureCrossReferenceParser.FindReferences("그림 3.2.1 참조");
        refs.Should().HaveCount(1);
        refs[0].ReferenceId.Should().Be("3.2.1");
    }
}
