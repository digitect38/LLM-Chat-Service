using FabCopilot.Contracts.Messages;
using FabCopilot.LlmService;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class SourceCitationTests
{
    [Fact]
    public void BuildSourceCitations_WithFileNameMetadata_GeneratesCitationSection()
    {
        var ragResults = new List<RetrievalResult>
        {
            new()
            {
                DocumentId = "doc-1",
                ChunkText = "CMP pad replacement guide.",
                Score = 0.85f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "cmp-maintenance.md" }
            }
        };

        var citations = LlmWorker.BuildSourceCitations(ragResults);

        citations.Should().Contain("참고 문서:");
        citations.Should().Contain("cmp-maintenance.md");
        citations.Should().Contain("0.850");
    }

    [Fact]
    public void BuildSourceCitations_EmptyList_ReturnsEmptyString()
    {
        var ragResults = new List<RetrievalResult>();

        var citations = LlmWorker.BuildSourceCitations(ragResults);

        citations.Should().BeEmpty();
    }

    [Fact]
    public void BuildSourceCitations_DuplicateDocumentNames_Deduplicated()
    {
        var ragResults = new List<RetrievalResult>
        {
            new()
            {
                DocumentId = "doc-1",
                ChunkText = "First chunk.",
                Score = 0.90f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "cmp-guide.md" }
            },
            new()
            {
                DocumentId = "doc-2",
                ChunkText = "Second chunk.",
                Score = 0.80f,
                Metadata = new Dictionary<string, object> { ["file_name"] = "cmp-guide.md" }
            }
        };

        var citations = LlmWorker.BuildSourceCitations(ragResults);

        // "cmp-guide.md" should appear only once despite two results with same file_name
        var occurrences = citations.Split("cmp-guide.md").Length - 1;
        occurrences.Should().Be(1);
    }

    [Fact]
    public void BuildSourceCitations_UnknownSourceExcluded_ReturnsEmptyString()
    {
        // Result with no metadata and empty DocumentId → ExtractSourceName returns "unknown" → excluded
        var ragResults = new List<RetrievalResult>
        {
            new()
            {
                DocumentId = "",
                ChunkText = "Some content.",
                Score = 0.70f,
                Metadata = new Dictionary<string, object>()
            }
        };

        var citations = LlmWorker.BuildSourceCitations(ragResults);

        citations.Should().BeEmpty();
    }
}
