using System.Text;
using FluentAssertions;
using FabCopilot.LlmService;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class FilterThinkBlocksEdgeCaseTests
{
    private static string Filter(string token, ref bool insideThinkBlock, StringBuilder tagBuffer)
        => LlmWorker.FilterThinkBlocks(token, ref insideThinkBlock, tagBuffer);

    [Fact]
    public void FilterThinkBlocks_NonThinkAngleBracket_PassedThrough()
    {
        var inside = false;
        var buf = new StringBuilder();
        var result = Filter("<div>hello</div>", ref inside, buf);
        result.Should().Contain("hello");
        result.Should().Contain("<div>");
    }

    [Fact]
    public void FilterThinkBlocks_UppercaseThink_Removed()
    {
        var inside = false;
        var buf = new StringBuilder();
        var result = Filter("<THINK>hidden content</THINK>visible", ref inside, buf);
        result.Should().NotContain("hidden content");
        result.Should().Contain("visible");
    }

    [Fact]
    public void FilterThinkBlocks_MixedCaseThink_Removed()
    {
        var inside = false;
        var buf = new StringBuilder();
        var result = Filter("<Think>mixed case hidden</Think>after", ref inside, buf);
        result.Should().NotContain("mixed case hidden");
        result.Should().Contain("after");
    }

    [Fact]
    public void FilterThinkBlocks_ThreeTokenBoundary_OpenTag()
    {
        var inside = false;
        var buf = new StringBuilder();

        // Split "<think>" across 3 tokens: "<" + "thi" + "nk>"
        var r1 = Filter("<", ref inside, buf);
        var r2 = Filter("thi", ref inside, buf);
        var r3 = Filter("nk>", ref inside, buf);

        // After processing all three, insideThinkBlock should be true
        inside.Should().BeTrue();
        // No think tag content should leak
        (r1 + r2 + r3).Should().BeEmpty();
    }

    [Fact]
    public void FilterThinkBlocks_ContentBetweenTwoBlocks_Preserved()
    {
        var inside = false;
        var buf = new StringBuilder();

        var r1 = Filter("<think>block1</think>", ref inside, buf);
        var r2 = Filter("visible", ref inside, buf);
        var r3 = Filter("<think>block2</think>", ref inside, buf);

        (r1 + r2 + r3).Should().Be("visible");
    }

    [Fact]
    public void FilterThinkBlocks_EmptyThinkBlock_NoOutput()
    {
        var inside = false;
        var buf = new StringBuilder();
        var result = Filter("<think></think>", ref inside, buf);
        result.Should().BeEmpty();
        inside.Should().BeFalse();
    }

    [Fact]
    public void FilterThinkBlocks_TagInsidePlainText_OnlyThinkRemoved()
    {
        var inside = false;
        var buf = new StringBuilder();
        var result = Filter("Hello<think>x</think>World", ref inside, buf);
        result.Should().Be("HelloWorld");
    }

    [Fact]
    public void FilterThinkBlocks_StatePersistence_AcrossMultipleCalls()
    {
        var inside = false;
        var buf = new StringBuilder();

        // Call 1: open tag
        var r1 = Filter("<think>", ref inside, buf);
        inside.Should().BeTrue();

        // Call 2: content inside think block (should be suppressed)
        var r2 = Filter("hidden reasoning content", ref inside, buf);

        // Call 3: close tag
        var r3 = Filter("</think>after", ref inside, buf);
        inside.Should().BeFalse();

        (r1 + r2 + r3).Should().Be("after");
    }

    [Fact]
    public void FilterThinkBlocks_LessThanInMath_NotConfused()
    {
        var inside = false;
        var buf = new StringBuilder();
        // "3 < 5" should pass through — '<' followed by ' ' is not a tag start
        // The '<' will be buffered, then '5' makes "<5" which doesn't match "<think>" or "</think>"
        // so it gets flushed
        var result = Filter("3 < 5 and x > 2", ref inside, buf);
        result.Should().Contain("3");
        result.Should().Contain("5");
    }
}
