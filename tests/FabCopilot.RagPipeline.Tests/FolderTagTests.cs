using FluentAssertions;
using FabCopilot.RagService.Services;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

/// <summary>
/// 폴더 기반 자동 태깅 테스트
/// </summary>
public class FolderTagTests
{
    // ──────────────────────────────────────────────────────────────
    // 경로 파싱 → 올바른 태그 추출
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractFolderTags_NestedPath_ExtractsMultipleTags()
    {
        var tags = DocumentIngestor.ExtractFolderTags("docs/cmp/troubleshooting/alarm-guide.md");

        tags.Should().Contain("cmp");
        tags.Should().Contain("troubleshooting");
    }

    [Fact]
    public void ExtractFolderTags_SingleFolder_ExtractsOneTag()
    {
        var tags = DocumentIngestor.ExtractFolderTags("cmp/alarm-guide.md");

        tags.Should().HaveCount(1);
        tags.Should().Contain("cmp");
    }

    // ──────────────────────────────────────────────────────────────
    // 필터링 대상 폴더 (docs, src) → 제외
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractFolderTags_GenericFolders_AreFiltered()
    {
        var tags = DocumentIngestor.ExtractFolderTags("docs/src/cmp/guide.md");

        tags.Should().NotContain("docs");
        tags.Should().NotContain("src");
        tags.Should().Contain("cmp");
    }

    [Theory]
    [InlineData("docs/file.md")]
    [InlineData("src/file.md")]
    [InlineData("resources/file.md")]
    [InlineData("data/file.md")]
    public void ExtractFolderTags_OnlyGenericFolder_ReturnsEmpty(string path)
    {
        var tags = DocumentIngestor.ExtractFolderTags(path);
        tags.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────
    // 파일명만 (폴더 없음) → 빈 리스트
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractFolderTags_FileNameOnly_ReturnsEmpty()
    {
        var tags = DocumentIngestor.ExtractFolderTags("alarm-guide.md");

        tags.Should().BeEmpty();
    }

    [Fact]
    public void ExtractFolderTags_EmptyPath_ReturnsEmpty()
    {
        DocumentIngestor.ExtractFolderTags("").Should().BeEmpty();
        DocumentIngestor.ExtractFolderTags(null!).Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────
    // 백슬래시 경로 (Windows) 정규화
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractFolderTags_BackslashPath_NormalizesCorrectly()
    {
        var tags = DocumentIngestor.ExtractFolderTags(@"docs\cmp\troubleshooting\alarm-guide.md");

        tags.Should().Contain("cmp");
        tags.Should().Contain("troubleshooting");
        tags.Should().NotContain("docs");
    }

    // ──────────────────────────────────────────────────────────────
    // 소문자 정규화
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractFolderTags_MixedCase_NormalizedToLower()
    {
        var tags = DocumentIngestor.ExtractFolderTags("CMP/TroubleShooting/guide.md");

        tags.Should().Contain("cmp");
        tags.Should().Contain("troubleshooting");
    }

    // ──────────────────────────────────────────────────────────────
    // InferDocType 폴더 폴백 테스트
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void InferDocType_FileName_MatchesDirectly()
    {
        DocumentIngestor.InferDocType("alarm-codes.md").Should().Be("alarm");
    }

    [Fact]
    public void InferDocType_FolderFallback_MatchesFolderName()
    {
        // File name "guide.md" doesn't match any type, but folder "troubleshooting" does
        DocumentIngestor.InferDocType("troubleshooting/guide.md").Should().Be("troubleshooting");
    }

    [Fact]
    public void InferDocType_NoMatch_ReturnsGeneral()
    {
        DocumentIngestor.InferDocType("readme.md").Should().Be("general");
    }
}
