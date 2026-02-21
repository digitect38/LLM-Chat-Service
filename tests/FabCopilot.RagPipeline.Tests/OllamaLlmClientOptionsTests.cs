using FabCopilot.Llm;
using FabCopilot.Llm.Models;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class OllamaLlmClientOptionsTests
{
    [Fact]
    public void BuildRequestOptions_NullLlmOptions_ReturnsDefaults()
    {
        var result = OllamaLlmClient.BuildRequestOptions(null);

        result.Temperature.Should().Be(0.1f);
        result.NumPredict.Should().Be(2048);
    }

    [Fact]
    public void BuildRequestOptions_CustomLlmOptions_ReturnsCustomValues()
    {
        var options = new LlmOptions
        {
            Temperature = 0.5f,
            MaxTokens = 1024
        };

        var result = OllamaLlmClient.BuildRequestOptions(options);

        result.Temperature.Should().Be(0.5f);
        result.NumPredict.Should().Be(1024);
    }

    [Fact]
    public void BuildRequestOptions_DefaultLlmOptions_ReturnsSameAsNull()
    {
        var options = new LlmOptions();

        var result = OllamaLlmClient.BuildRequestOptions(options);

        result.Temperature.Should().Be(0.1f);
        result.NumPredict.Should().Be(2048);
    }
}
