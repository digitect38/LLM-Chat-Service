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
    public void BuildSourceCitations_FallbackToFilePath()
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
        citations.Should().Contain("/docs/guide.md");
    }

    [Fact]
    public void BuildSourceCitations_FallbackToDocumentId()
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
    public void BuildSourceCitations_FallbackToResultDocumentId()
    {
        var results = new List<RetrievalResult>
        {
            new()
            {
                DocumentId = "result-doc-id-fallback",
                ChunkText = "some text",
                Score = 0.8f,
                Metadata = new Dictionary<string, object>()
            }
        };

        var citations = LlmWorker.BuildSourceCitations(results);
        citations.Should().Contain("result-doc-id-fallback");
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
        citations.Should().Contain("file-a.md");
        citations.Should().Contain("file-b.md");
        citations.Should().Contain("file-c.md");
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
    public void BuildSourceCitations_ContainsMarkdownSeparator()
    {
        var results = new List<RetrievalResult>
        {
            new() { ChunkText = "text", Score = 0.9f, Metadata = new Dictionary<string, object> { ["file_name"] = "test.md" } }
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
        // Should appear exactly once in the citation list
        var count = citations.Split("same-file.md").Length - 1;
        count.Should().Be(1);
    }
}
