using FabCopilot.Llm.Interfaces;
using FabCopilot.Llm.Models;
using FabCopilot.RagService.Services;
using FabCopilot.VectorStore.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class LlmRerankerTests
{
    private static VectorSearchResult MakeResult(string id, float score)
        => new(id, score, new Dictionary<string, object> { ["text"] = $"Content for {id}" });

    private static LlmReranker CreateReranker(Mock<ILlmClient> mockLlm)
    {
        var mockLogger = new Mock<ILogger<LlmReranker>>();
        return new LlmReranker(mockLlm.Object, mockLogger.Object);
    }

    [Fact]
    public async Task RerankAsync_ValidJsonResponse_RerankedByLlmScore()
    {
        var mockLlm = new Mock<ILlmClient>();
        // LLM says: doc index 2 has highest score, then 0, then 1
        var jsonResponse = """[{"index":2,"score":9},{"index":0,"score":7},{"index":1,"score":3}]""";
        mockLlm
            .Setup(x => x.CompleteChatAsync(
                It.IsAny<IReadOnlyList<LlmChatMessage>>(),
                It.IsAny<LlmOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsonResponse);

        var reranker = CreateReranker(mockLlm);
        var candidates = new List<VectorSearchResult>
        {
            MakeResult("doc-0", 0.8f),
            MakeResult("doc-1", 0.7f),
            MakeResult("doc-2", 0.6f)
        };

        var result = await reranker.RerankAsync("test query", candidates, 2, CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be("doc-2"); // highest LLM score
        result[1].Id.Should().Be("doc-0"); // second highest
    }

    [Fact]
    public async Task RerankAsync_MarkdownCodeFenceResponse_ParsedCorrectly()
    {
        var mockLlm = new Mock<ILlmClient>();
        var jsonResponse = "```json\n[{\"index\":1,\"score\":9},{\"index\":0,\"score\":5}]\n```";
        mockLlm
            .Setup(x => x.CompleteChatAsync(
                It.IsAny<IReadOnlyList<LlmChatMessage>>(),
                It.IsAny<LlmOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsonResponse);

        var reranker = CreateReranker(mockLlm);
        var candidates = new List<VectorSearchResult>
        {
            MakeResult("doc-0", 0.9f),
            MakeResult("doc-1", 0.8f),
            MakeResult("doc-2", 0.7f)
        };

        var result = await reranker.RerankAsync("test query", candidates, 2, CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be("doc-1"); // highest LLM score
        result[1].Id.Should().Be("doc-0");
    }

    [Fact]
    public async Task RerankAsync_InvalidJson_FallsBackToOriginalOrder()
    {
        var mockLlm = new Mock<ILlmClient>();
        mockLlm
            .Setup(x => x.CompleteChatAsync(
                It.IsAny<IReadOnlyList<LlmChatMessage>>(),
                It.IsAny<LlmOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("This is not valid JSON at all");

        var reranker = CreateReranker(mockLlm);
        var candidates = new List<VectorSearchResult>
        {
            MakeResult("doc-0", 0.9f),
            MakeResult("doc-1", 0.8f),
            MakeResult("doc-2", 0.7f)
        };

        var result = await reranker.RerankAsync("test query", candidates, 2, CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be("doc-0"); // original order preserved
        result[1].Id.Should().Be("doc-1");
    }

    [Fact]
    public async Task RerankAsync_EmptyCandidates_ReturnsEmpty()
    {
        var mockLlm = new Mock<ILlmClient>();
        var reranker = CreateReranker(mockLlm);

        var result = await reranker.RerankAsync("test query", new List<VectorSearchResult>(), 5, CancellationToken.None);

        result.Should().BeEmpty();
        mockLlm.Verify(
            x => x.CompleteChatAsync(
                It.IsAny<IReadOnlyList<LlmChatMessage>>(),
                It.IsAny<LlmOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RerankAsync_CandidatesCountLessThanOrEqualTopK_ReturnedAsIsWithoutCallingLlm()
    {
        var mockLlm = new Mock<ILlmClient>();
        var reranker = CreateReranker(mockLlm);
        var candidates = new List<VectorSearchResult>
        {
            MakeResult("doc-0", 0.9f),
            MakeResult("doc-1", 0.8f)
        };

        var result = await reranker.RerankAsync("test query", candidates, 5, CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be("doc-0");
        result[1].Id.Should().Be("doc-1");
        mockLlm.Verify(
            x => x.CompleteChatAsync(
                It.IsAny<IReadOnlyList<LlmChatMessage>>(),
                It.IsAny<LlmOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RerankAsync_OutOfRangeIndex_IgnoredAndFallsBackToOriginalOrder()
    {
        var mockLlm = new Mock<ILlmClient>();
        // Response contains only an out-of-range index
        var jsonResponse = """[{"index":999,"score":10}]""";
        mockLlm
            .Setup(x => x.CompleteChatAsync(
                It.IsAny<IReadOnlyList<LlmChatMessage>>(),
                It.IsAny<LlmOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsonResponse);

        var reranker = CreateReranker(mockLlm);
        var candidates = new List<VectorSearchResult>
        {
            MakeResult("doc-0", 0.9f),
            MakeResult("doc-1", 0.8f),
            MakeResult("doc-2", 0.7f)
        };

        var result = await reranker.RerankAsync("test query", candidates, 2, CancellationToken.None);

        // Out-of-range index filtered out → empty ranked list → fallback to original order
        result.Should().HaveCount(2);
        result[0].Id.Should().Be("doc-0");
        result[1].Id.Should().Be("doc-1");
    }
}
