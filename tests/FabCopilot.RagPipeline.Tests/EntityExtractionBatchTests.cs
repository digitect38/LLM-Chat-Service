using FabCopilot.Contracts.Models;
using FabCopilot.Llm.Interfaces;
using FabCopilot.Llm.Models;
using FabCopilot.RagService.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class EntityExtractionBatchTests
{
    [Fact]
    public void BuildBatches_SingleSmallChunk_OneBatch()
    {
        var chunks = new List<string> { "Short chunk text." };
        var batches = LlmEntityExtractor.BuildBatches(chunks, 1800);

        batches.Should().HaveCount(1);
        batches[0].Should().Contain("Short chunk text.");
    }

    [Fact]
    public void BuildBatches_MultipleSmallChunks_CombinedIntoOneBatch()
    {
        var chunks = new List<string>
        {
            "Chunk one about CMP.",
            "Chunk two about polishing pad.",
            "Chunk three about slurry."
        };

        var batches = LlmEntityExtractor.BuildBatches(chunks, 1800);

        batches.Should().HaveCount(1);
        batches[0].Should().Contain("Chunk one");
        batches[0].Should().Contain("Chunk two");
        batches[0].Should().Contain("Chunk three");
    }

    [Fact]
    public void BuildBatches_LargeChunks_SplitIntoMultipleBatches()
    {
        // Create chunks that each are close to the limit
        var largeChunk1 = new string('A', 1000);
        var largeChunk2 = new string('B', 1000);
        var largeChunk3 = new string('C', 500);

        var batches = LlmEntityExtractor.BuildBatches(
            new List<string> { largeChunk1, largeChunk2, largeChunk3 }, 1800);

        batches.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void BuildBatches_EmptyList_ReturnsEmpty()
    {
        var batches = LlmEntityExtractor.BuildBatches(new List<string>(), 1800);
        batches.Should().BeEmpty();
    }

    [Fact]
    public void BuildBatches_ChunkExceedsMaxChars_Truncated()
    {
        var hugeChunk = new string('X', 5000);
        var batches = LlmEntityExtractor.BuildBatches(new List<string> { hugeChunk }, 1800);

        batches.Should().HaveCount(1);
        batches[0].Length.Should().BeLessThanOrEqualTo(1800);
    }

    [Fact]
    public async Task ExtractFromBatchAsync_ParsesJsonResponse()
    {
        // Arrange
        var mockLlm = new Mock<ILlmClient>();
        mockLlm.Setup(x => x.CompleteChatAsync(It.IsAny<List<LlmChatMessage>>(), It.IsAny<LlmOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                {"entities": [{"name": "CMP", "type": "Equipment"}, {"name": "slurry", "type": "Component"}],
                 "relations": [{"source": "CMP", "target": "slurry", "type": "UsedIn"}]}
                """);

        var extractor = new LlmEntityExtractor(mockLlm.Object, NullLogger<LlmEntityExtractor>.Instance);

        // Act
        var (entities, relations) = await extractor.ExtractFromBatchAsync(
            new List<string> { "CMP uses slurry." }, CancellationToken.None);

        // Assert
        entities.Should().HaveCount(2);
        entities.Should().Contain(e => e.Name == "CMP" && e.Type == "Equipment");
        entities.Should().Contain(e => e.Name == "slurry" && e.Type == "Component");
        relations.Should().HaveCount(1);
        relations[0].RelationType.Should().Be("UsedIn");
    }

    [Fact]
    public async Task ExtractFromBatchAsync_HandlesCodeFences()
    {
        // Arrange
        var mockLlm = new Mock<ILlmClient>();
        mockLlm.Setup(x => x.CompleteChatAsync(It.IsAny<List<LlmChatMessage>>(), It.IsAny<LlmOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                ```json
                {"entities": [{"name": "pad", "type": "Component"}], "relations": []}
                ```
                """);

        var extractor = new LlmEntityExtractor(mockLlm.Object, NullLogger<LlmEntityExtractor>.Instance);

        // Act
        var (entities, relations) = await extractor.ExtractFromBatchAsync(
            new List<string> { "About polishing pad." }, CancellationToken.None);

        // Assert
        entities.Should().HaveCount(1);
        entities[0].Name.Should().Be("pad");
    }

    [Fact]
    public async Task ExtractFromBatchAsync_InvalidJson_ReturnsEmpty()
    {
        // Arrange
        var mockLlm = new Mock<ILlmClient>();
        mockLlm.Setup(x => x.CompleteChatAsync(It.IsAny<List<LlmChatMessage>>(), It.IsAny<LlmOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("This is not JSON at all");

        var extractor = new LlmEntityExtractor(mockLlm.Object, NullLogger<LlmEntityExtractor>.Instance);

        // Act
        var (entities, relations) = await extractor.ExtractFromBatchAsync(
            new List<string> { "Some text." }, CancellationToken.None);

        // Assert
        entities.Should().BeEmpty();
        relations.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractFromBatchAsync_EmptyChunks_ReturnsEmpty()
    {
        var mockLlm = new Mock<ILlmClient>();
        var extractor = new LlmEntityExtractor(mockLlm.Object, NullLogger<LlmEntityExtractor>.Instance);

        var (entities, relations) = await extractor.ExtractFromBatchAsync(
            new List<string>(), CancellationToken.None);

        entities.Should().BeEmpty();
        relations.Should().BeEmpty();
        mockLlm.Verify(x => x.CompleteChatAsync(It.IsAny<List<LlmChatMessage>>(), It.IsAny<LlmOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExtractFromBatchAsync_LlmException_ContinuesWithNextBatch()
    {
        // Arrange: first call throws, second succeeds
        var callCount = 0;
        var mockLlm = new Mock<ILlmClient>();
        mockLlm.Setup(x => x.CompleteChatAsync(It.IsAny<List<LlmChatMessage>>(), It.IsAny<LlmOptions?>(), It.IsAny<CancellationToken>()))
            .Returns<List<LlmChatMessage>, LlmOptions?, CancellationToken>((_, _, _) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new Exception("LLM timeout");
                return Task.FromResult("""{"entities": [{"name": "CMP", "type": "Equipment"}], "relations": []}""");
            });

        var extractor = new LlmEntityExtractor(mockLlm.Object, NullLogger<LlmEntityExtractor>.Instance);

        // Create two chunks that will be in separate batches (each large enough to force split)
        var chunk1 = new string('A', 1500);
        var chunk2 = new string('B', 1000) + " CMP equipment overview.";

        // Act
        var (entities, relations) = await extractor.ExtractFromBatchAsync(
            new List<string> { chunk1, chunk2 }, CancellationToken.None);

        // Assert - first batch failed but second succeeded
        entities.Should().HaveCount(1);
        entities[0].Name.Should().Be("CMP");
    }
}
