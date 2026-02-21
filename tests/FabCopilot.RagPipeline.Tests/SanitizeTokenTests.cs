using FabCopilot.LlmService;
using FluentAssertions;
using System.Reflection;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class SanitizeTokenTests
{
    private static string? InvokeSanitizeToken(string? token)
    {
        var method = typeof(LlmWorker).GetMethod("SanitizeToken",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (string?)method!.Invoke(null, [token]);
    }

    [Fact]
    public void NormalText_Unchanged()
    {
        var result = InvokeSanitizeToken("Hello World");

        result.Should().Be("Hello World");
    }

    [Fact]
    public void ImStart_Removed()
    {
        var result = InvokeSanitizeToken("<|im_start|>");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ImEnd_Removed()
    {
        var result = InvokeSanitizeToken("<|im_end|>");

        result.Should().BeEmpty();
    }

    [Fact]
    public void EndOfText_Removed()
    {
        var result = InvokeSanitizeToken("<|endoftext|>");

        result.Should().BeEmpty();
    }

    [Fact]
    public void SpecialMarkerInMiddle_MarkerRemovedTextPreserved()
    {
        var result = InvokeSanitizeToken("Hello<|im_start|>World");

        result.Should().Be("HelloWorld");
    }

    [Fact]
    public void EmptyString_ReturnsEmpty()
    {
        var result = InvokeSanitizeToken("");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Null_ReturnsNull()
    {
        var result = InvokeSanitizeToken(null);

        result.Should().BeNull();
    }

    [Fact]
    public void MultipleSpecialTokens_AllRemoved()
    {
        var result = InvokeSanitizeToken("<|im_start|>system<|im_end|>content<|endoftext|>");

        result.Should().Be("systemcontent");
    }
}
