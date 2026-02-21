using FabCopilot.Llm.Interfaces;
using FabCopilot.Llm.Models;
using FabCopilot.RagService.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class QueryRewriterTests
{
    private static LlmQueryRewriter CreateRewriter(Mock<ILlmClient> mockLlm)
    {
        var mockLogger = new Mock<ILogger<LlmQueryRewriter>>();
        return new LlmQueryRewriter(mockLlm.Object, mockLogger.Object);
    }

    [Fact]
    public async Task RewriteAsync_NormalResponse_ReturnsTrimmedResult()
    {
        var mockLlm = new Mock<ILlmClient>();
        mockLlm
            .Setup(x => x.CompleteChatAsync(
                It.IsAny<IReadOnlyList<LlmChatMessage>>(),
                It.IsAny<LlmOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("  CMP Chemical Mechanical Polishing 패드 교체 주기  ");

        var rewriter = CreateRewriter(mockLlm);

        var result = await rewriter.RewriteAsync("CMP 패드 교체", CancellationToken.None);

        result.Should().Be("CMP Chemical Mechanical Polishing 패드 교체 주기");
    }

    [Fact]
    public async Task RewriteAsync_EmptyResponse_ReturnsOriginalQuery()
    {
        var mockLlm = new Mock<ILlmClient>();
        mockLlm
            .Setup(x => x.CompleteChatAsync(
                It.IsAny<IReadOnlyList<LlmChatMessage>>(),
                It.IsAny<LlmOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("   ");

        var rewriter = CreateRewriter(mockLlm);

        var result = await rewriter.RewriteAsync("원본 질문", CancellationToken.None);

        result.Should().Be("원본 질문");
    }

    [Fact]
    public async Task RewriteAsync_ExceptionThrown_ReturnsOriginalQuery()
    {
        var mockLlm = new Mock<ILlmClient>();
        mockLlm
            .Setup(x => x.CompleteChatAsync(
                It.IsAny<IReadOnlyList<LlmChatMessage>>(),
                It.IsAny<LlmOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Ollama connection failed"));

        var rewriter = CreateRewriter(mockLlm);

        var result = await rewriter.RewriteAsync("원본 질문", CancellationToken.None);

        result.Should().Be("원본 질문");
    }

    [Fact]
    public async Task RewriteAsync_SystemPromptContainsSemiconductorFabDomain()
    {
        var mockLlm = new Mock<ILlmClient>();
        IReadOnlyList<LlmChatMessage>? capturedMessages = null;
        mockLlm
            .Setup(x => x.CompleteChatAsync(
                It.IsAny<IReadOnlyList<LlmChatMessage>>(),
                It.IsAny<LlmOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<LlmChatMessage>, LlmOptions?, CancellationToken>(
                (msgs, _, _) => capturedMessages = msgs)
            .ReturnsAsync("rewritten query");

        var rewriter = CreateRewriter(mockLlm);

        await rewriter.RewriteAsync("테스트 질문", CancellationToken.None);

        capturedMessages.Should().NotBeNull();
        capturedMessages!.Should().Contain(m => m.Content.Contains("반도체"));
    }
}
