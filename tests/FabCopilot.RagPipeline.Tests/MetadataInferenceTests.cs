using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class MetadataInferenceTests
{
    // ─── InferDocType ───────────────────────────────────────────────

    [Theory]
    [InlineData("cmp-alarm-codes.md", "alarm")]
    [InlineData("error-handling-guide.md", "alarm")]
    [InlineData("fault-diagnosis.md", "alarm")]
    [InlineData("troubleshooting-guide.md", "troubleshooting")]
    [InlineData("진단-가이드.md", "troubleshooting")]
    [InlineData("maintenance-schedule.md", "maintenance")]
    [InlineData("유지보수-절차.md", "maintenance")]
    [InlineData("pad-replacement-procedure.md", "procedure")]
    [InlineData("패드-교체-절차.md", "procedure")]
    [InlineData("cmp-spec-sheet.md", "specification")]
    [InlineData("parameter-guide.md", "specification")]
    [InlineData("cmp-overview.md", "overview")]
    [InlineData("system-개요.md", "overview")]
    [InlineData("glossary.md", "glossary")]
    [InlineData("parts-list.md", "parts")]
    [InlineData("소모품-관리.md", "parts")]
    [InlineData("cmp-general-info.md", "general")]
    [InlineData("readme.md", "general")]
    public void InferDocType_MatchesExpected(string fileName, string expectedType)
    {
        DocumentIngestor.InferDocType(fileName).Should().Be(expectedType);
    }

    // ─── DetectLanguage ─────────────────────────────────────────────

    [Theory]
    [InlineData("CMP 패드 교체 절차를 설명합니다", "ko")]
    [InlineData("이 문서는 한국어로 작성되었습니다", "ko")]
    [InlineData("This is an English document about CMP", "en")]
    [InlineData("The pad replacement procedure is described here", "en")]
    public void DetectLanguage_CorrectlyIdentifies(string text, string expectedLang)
    {
        DocumentIngestor.DetectLanguage(text).Should().Be(expectedLang);
    }

    [Fact]
    public void DetectLanguage_Empty_ReturnsUnknown()
    {
        DocumentIngestor.DetectLanguage("").Should().Be("unknown");
        DocumentIngestor.DetectLanguage("   ").Should().Be("unknown");
    }

    [Fact]
    public void DetectLanguage_NumbersOnly_ReturnsUnknown()
    {
        DocumentIngestor.DetectLanguage("12345 67890").Should().Be("unknown");
    }

    [Fact]
    public void DetectLanguage_MixedContent_MajorityWins()
    {
        // More Korean than English
        var koreanDominant = "CMP 장비의 패드를 교체하는 방법을 설명합니다";
        DocumentIngestor.DetectLanguage(koreanDominant).Should().Be("ko");

        // More English than Korean
        var englishDominant = "The CMP polishing pad replacement guide for 패드";
        DocumentIngestor.DetectLanguage(englishDominant).Should().Be("en");
    }

    // ─── ExtractSectionFromChunk ────────────────────────────────────

    [Fact]
    public void ExtractSectionFromChunk_WithHeaderPath_ExtractsChapterAndSection()
    {
        var chunk = "[## 1. 패드 교체 절차 > ### 1.1 교체 판단 기준]\n패드 교체 시기를 판단합니다.";
        var (chapter, section) = DocumentIngestor.ExtractSectionFromChunk(chunk);

        chapter.Should().Be("1. 패드 교체 절차");
        section.Should().Be("1.1 교체 판단 기준");
    }

    [Fact]
    public void ExtractSectionFromChunk_SingleHeader_ChapterOnly()
    {
        var chunk = "[## 개요]\n시스템 개요입니다.";
        var (chapter, section) = DocumentIngestor.ExtractSectionFromChunk(chunk);

        chapter.Should().Be("개요");
        section.Should().BeNull();
    }

    [Fact]
    public void ExtractSectionFromChunk_NoHeader_ReturnsNulls()
    {
        var chunk = "일반 텍스트 청크입니다.";
        var (chapter, section) = DocumentIngestor.ExtractSectionFromChunk(chunk);

        chapter.Should().BeNull();
        section.Should().BeNull();
    }

    [Fact]
    public void ExtractSectionFromChunk_DeepNesting_ExtractsFirstAndLast()
    {
        var chunk = "[# Title > ## Chapter > ### Section > #### Subsection]\nContent here.";
        var (chapter, section) = DocumentIngestor.ExtractSectionFromChunk(chunk);

        chapter.Should().Be("Title");
        section.Should().Be("Subsection");
    }
}
