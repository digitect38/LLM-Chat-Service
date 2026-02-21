using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class PdfPageExtractionTests
{
    [Fact]
    public void IsPdf_WithPdfExtension_ReturnsTrue()
    {
        FileTextExtractor.IsPdf("document.pdf").Should().BeTrue();
        FileTextExtractor.IsPdf("DOCUMENT.PDF").Should().BeTrue();
        FileTextExtractor.IsPdf("/path/to/file.Pdf").Should().BeTrue();
    }

    [Fact]
    public void IsPdf_WithNonPdfExtension_ReturnsFalse()
    {
        FileTextExtractor.IsPdf("document.md").Should().BeFalse();
        FileTextExtractor.IsPdf("document.txt").Should().BeFalse();
        FileTextExtractor.IsPdf("document.docx").Should().BeFalse();
    }

    [Fact]
    public void IsSupported_IncludesPdf()
    {
        FileTextExtractor.IsSupported("test.pdf").Should().BeTrue();
        FileTextExtractor.IsSupported("test.PDF").Should().BeTrue();
    }

    [Fact]
    public void InferDocType_PdfFilesAreClassifiedCorrectly()
    {
        // PDF files should be classified by their filename keywords too
        DocumentIngestor.InferDocType("alarm-codes.pdf").Should().Be("alarm");
        DocumentIngestor.InferDocType("maintenance-guide.pdf").Should().Be("maintenance");
        DocumentIngestor.InferDocType("general-info.pdf").Should().Be("general");
    }

    [Fact]
    public void ChunkText_PreservesContentForPdfPages()
    {
        // Simulate a PDF page's text content
        var pageText = "CMP 장비의 패드 교체 절차입니다. " +
                       "1단계: 장비를 정지합니다. " +
                       "2단계: 기존 패드를 제거합니다. " +
                       "3단계: 새 패드를 장착합니다.";

        var chunks = DocumentIngestor.ChunkText(pageText, 512, 128);

        chunks.Should().NotBeEmpty();
        chunks[0].Should().Contain("패드 교체");
    }
}
