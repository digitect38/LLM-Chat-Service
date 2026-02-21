using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class BuildDisplayRefEdgeCaseTests
{
    [Fact]
    public void AllNullExceptDocId_ReturnsDocIdOnly()
    {
        var result = LlmWorker.BuildDisplayRef("DOC-001", null, null, null, null);

        result.Should().Be("DOC-001");
    }

    [Fact]
    public void DocIdPlusChapterOnly_ReturnsDocIdDashChapter()
    {
        var result = LlmWorker.BuildDisplayRef("DOC-001", "Ch1", null, null, null);

        result.Should().Be("DOC-001-Ch1");
    }

    [Fact]
    public void DocIdPlusSectionOnly_ReturnsPrefixedSection()
    {
        var result = LlmWorker.BuildDisplayRef("DOC-001", null, "1.1", null, null);

        result.Should().Be("DOC-001-S1.1");
    }

    [Fact]
    public void DocIdChapterSectionLineRange_FullFormat()
    {
        var lineRange = new LineRangeInfo { From = 10, To = 20 };

        var result = LlmWorker.BuildDisplayRef("DOC-001", "Ch3", "3.2", lineRange, null);

        result.Should().Be("DOC-001-Ch3-S3.2-{Line:10-20}");
    }

    [Fact]
    public void DocIdChapterSectionPage_NoLineRange_PageFallback()
    {
        var result = LlmWorker.BuildDisplayRef("DOC-001", "Ch3", "3.2", null, 5);

        result.Should().Be("DOC-001-Ch3-S3.2-{Page:5}");
    }

    [Fact]
    public void LineRange_FromEqualsTo_SingleLine()
    {
        var lineRange = new LineRangeInfo { From = 42, To = 42 };

        var result = LlmWorker.BuildDisplayRef("DOC-001", null, null, lineRange, null);

        result.Should().Be("DOC-001-{Line:42-42}");
    }

    [Fact]
    public void EmptyChapter_TreatedAsNull()
    {
        var result = LlmWorker.BuildDisplayRef("DOC-001", "", null, null, null);

        // Empty string is treated as null by string.IsNullOrEmpty check
        result.Should().Be("DOC-001");
    }

    [Fact]
    public void EmptySection_TreatedAsNull()
    {
        var result = LlmWorker.BuildDisplayRef("DOC-001", "Ch1", "", null, null);

        // Empty section is skipped
        result.Should().Be("DOC-001-Ch1");
    }

    [Fact]
    public void PageZero_StillIncluded()
    {
        var result = LlmWorker.BuildDisplayRef("DOC-001", null, null, null, 0);

        result.Should().Be("DOC-001-{Page:0}");
    }

    [Fact]
    public void VeryLongDocIdAndChapter_NoTruncation()
    {
        var longDocId = new string('D', 100);
        var longChapter = new string('C', 100);

        var result = LlmWorker.BuildDisplayRef(longDocId, longChapter, null, null, null);

        result.Should().Be($"{longDocId}-{longChapter}");
        result.Length.Should().Be(201); // 100 + 1 dash + 100
    }
}
