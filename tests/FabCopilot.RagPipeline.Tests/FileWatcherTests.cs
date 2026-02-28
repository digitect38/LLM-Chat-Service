using FabCopilot.RagService.Services;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class FileWatcherTests
{
    [Theory]
    [InlineData("/watch/guide.md", "/watch", "guide.md")]
    [InlineData("/watch/subfolder/guide.md", "/watch", "subfolder/guide.md")]
    [InlineData("/watch/Sub/Deep/file.txt", "/watch", "sub/deep/file.txt")]
    public void GetDocumentId_ReturnsNormalizedRelativePath(string filePath, string watchFolder, string expected)
    {
        var result = FileWatcherIngestorService.GetDocumentId(filePath, watchFolder);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("doc.md", true)]
    [InlineData("doc.txt", true)]
    [InlineData("doc.pdf", true)]
    [InlineData("doc.MD", true)]
    [InlineData("doc.TXT", true)]
    [InlineData("doc.PDF", true)]
    [InlineData("doc.png", true)]
    [InlineData("doc.jpg", true)]
    [InlineData("doc.jpeg", true)]
    [InlineData("doc.bmp", true)]
    [InlineData("doc.tiff", true)]
    [InlineData("doc.docx", false)]
    [InlineData("doc.xlsx", false)]
    [InlineData("doc.cs", false)]
    [InlineData("doc", false)]
    public void IsSupported_ReturnsCorrectly(string fileName, bool expected)
    {
        var result = FileTextExtractor.IsSupported(fileName);

        result.Should().Be(expected);
    }

    [Fact]
    public void GetDocumentId_AlwaysUsesForwardSlashesAndLowerCase()
    {
        // Simulate a Windows-style path
        var result = FileWatcherIngestorService.GetDocumentId(
            "/watch/SubFolder/MyDoc.MD", "/watch");

        result.Should().NotContain("\\");
        result.Should().Be("subfolder/mydoc.md");
    }
}
