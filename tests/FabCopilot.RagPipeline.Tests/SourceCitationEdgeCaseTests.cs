using FluentAssertions;
using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class SourceCitationEdgeCaseTests
{
    [Fact]
    public void BuildSourceCitations_NullList_ReturnsEmpty()
    {
        var result = LlmWorker.BuildSourceCitations(null!);
        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildSourceCitations_InternalFilePath_IncludedWithoutExtension()
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                DocumentId = "doc-1",
                ChunkText = "some text",
                Score = 0.8f,
                Metadata = new Dictionary<string, object> { ["file_path"] = "/docs/guide.md" }
            }
        };

        var citations = LlmWorker.BuildSourceCitations(results);

        citations.Should().Contain("참고 문서");
        citations.Should().NotContain(".md");
    }

    [Fact]
    public void BuildSourceCitations_PdfFilePath_IncludedWithoutExtension()
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                DocumentId = "doc-1",
                ChunkText = "some text",
                Score = 0.8f,
                Metadata = new Dictionary<string, object> { ["file_path"] = "/docs/guide.pdf" }
            }
        };

        var citations = LlmWorker.BuildSourceCitations(results);
        citations.Should().Contain("guide");
        citations.Should().NotContain(".pdf");
    }

    [Fact]
    public void BuildSourceCitations_DocumentIdWithoutExtension_Included()
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                DocumentId = "doc-1",
                ChunkText = "some text",
                Score = 0.8f,
                Metadata = new Dictionary<string, object> { ["document_id"] = "my-special-doc" }
            }
        };

        var citations = LlmWorker.BuildSourceCitations(results);

        citations.Should().Contain("my-special-doc");
    }

    [Fact]
    public void BuildSourceCitations_WithUrlMetadata_Included()
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                DocumentId = "doc-1",
                ChunkText = "some text",
                Score = 0.8f,
                Metadata = new Dictionary<string, object>
                {
                    ["document_id"] = "my-special-doc",
                    ["url"] = "https://example.com/doc"
                }
            }
        };

        var citations = LlmWorker.BuildSourceCitations(results);
        citations.Should().Contain("참고 문서");
    }

    [Fact]
    public void BuildSourceCitations_ThreeDistinctSources_AllListed()
    {
        var results = new List<RetrievalResult>
        {
            new() { ChunkText = "a", Score = 0.9f, Metadata = new Dictionary<string, object> { ["file_name"] = "file-a.md" } },
            new() { ChunkText = "b", Score = 0.8f, Metadata = new Dictionary<string, object> { ["file_name"] = "file-b.md" } },
            new() { ChunkText = "c", Score = 0.7f, Metadata = new Dictionary<string, object> { ["file_name"] = "file-c.md" } }
        };

        var citations = LlmWorker.BuildSourceCitations(results);
        citations.Should().Contain("file-a");
        citations.Should().Contain("file-b");
        citations.Should().Contain("file-c");
    }

    [Fact]
    public void BuildSourceCitations_AllUnknownSources_ReturnsEmpty()
    {
        var results = new List<RetrievalResult>
        {
            new() { DocumentId = "", ChunkText = "a", Score = 0.9f, Metadata = new Dictionary<string, object>() },
            new() { DocumentId = "", ChunkText = "b", Score = 0.8f, Metadata = new Dictionary<string, object>() }
        };

        var citations = LlmWorker.BuildSourceCitations(results);
        citations.Should().BeEmpty();
    }

    [Fact]
    public void BuildSourceCitations_PdfContainsMarkdownSeparator()
    {
        var results = new List<RetrievalResult>
        {
            new() { ChunkText = "text", Score = 0.9f, Metadata = new Dictionary<string, object> { ["file_name"] = "test.pdf" } }
        };

        var citations = LlmWorker.BuildSourceCitations(results);
        citations.Should().Contain("---");
    }

    [Fact]
    public void BuildSourceCitations_DuplicateFileNames_Deduplicated()
    {
        var results = new List<RetrievalResult>
        {
            new() { ChunkText = "a", Score = 0.9f, Metadata = new Dictionary<string, object> { ["file_name"] = "same-file.md" } },
            new() { ChunkText = "b", Score = 0.8f, Metadata = new Dictionary<string, object> { ["file_name"] = "same-file.md" } },
            new() { ChunkText = "c", Score = 0.7f, Metadata = new Dictionary<string, object> { ["file_name"] = "same-file.md" } }
        };

        var citations = LlmWorker.BuildSourceCitations(results);
        var count = citations.Split("same-file").Length - 1;
        count.Should().Be(1);
    }

    [Fact]
    public void BuildSourceCitations_AllFileTypes_ShownWithoutExtension()
    {
        var results = new List<RetrievalResult>
        {
            new() { ChunkText = "a", Score = 0.9f, Metadata = new Dictionary<string, object> { ["file_name"] = "file-a.md" } },
            new() { ChunkText = "b", Score = 0.8f, Metadata = new Dictionary<string, object> { ["file_name"] = "file-b.pdf" } },
            new() { ChunkText = "c", Score = 0.7f, Metadata = new Dictionary<string, object> { ["file_name"] = "file-c.txt" } }
        };

        var citations = LlmWorker.BuildSourceCitations(results);
        citations.Should().Contain("file-a");
        citations.Should().Contain("file-b");
        citations.Should().Contain("file-c");
        citations.Should().NotContain(".md");
        citations.Should().NotContain(".pdf");
        citations.Should().NotContain(".txt");
    }
}
