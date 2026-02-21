using System.Text;
using FabCopilot.LlmService;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class FilterThinkBlocksTests
{
    [Fact]
    public void FilterThinkBlocks_FullThinkBlock_CompletelyRemoved()
    {
        var insideThink = false;
        var tagBuffer = new StringBuilder();

        var output = LlmWorker.FilterThinkBlocks("<think>reasoning</think>", ref insideThink, tagBuffer);

        output.Should().BeEmpty();
        insideThink.Should().BeFalse();
    }

    [Fact]
    public void FilterThinkBlocks_TokenBoundarySpanning_HandledCorrectly()
    {
        var insideThink = false;
        var tagBuffer = new StringBuilder();

        // First token: partial opening tag
        var output1 = LlmWorker.FilterThinkBlocks("<thi", ref insideThink, tagBuffer);
        // Second token: completes opening tag, has content, then closing tag and rest
        var output2 = LlmWorker.FilterThinkBlocks("nk>content</think>rest", ref insideThink, tagBuffer);

        output1.Should().BeEmpty();
        output2.Should().Be("rest");
    }

    [Fact]
    public void FilterThinkBlocks_PlainText_PassesThroughUnchanged()
    {
        var insideThink = false;
        var tagBuffer = new StringBuilder();

        var output = LlmWorker.FilterThinkBlocks("Hello, world!", ref insideThink, tagBuffer);

        output.Should().Be("Hello, world!");
    }

    [Fact]
    public void FilterThinkBlocks_MultipleThinkBlocks_AllRemoved()
    {
        var insideThink = false;
        var tagBuffer = new StringBuilder();

        var output = LlmWorker.FilterThinkBlocks(
            "before<think>a</think>mid<think>b</think>after", ref insideThink, tagBuffer);

        output.Should().Be("beforemidafter");
    }

    [Fact]
    public void FilterThinkBlocks_IncompleteClosingTag_ContentSuppressed()
    {
        var insideThink = false;
        var tagBuffer = new StringBuilder();

        // Opening think tag present but no closing tag — all content after <think> is suppressed
        var output = LlmWorker.FilterThinkBlocks("<think>hidden content with no end", ref insideThink, tagBuffer);

        output.Should().BeEmpty();
        insideThink.Should().BeTrue();
    }
}
