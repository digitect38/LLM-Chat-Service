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
        result.NumPredict.Should().Be(4096);
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
        result.NumPredict.Should().Be(4096);
    }

    // ─── Long response tests ─────────────────────────────────────────

    [Fact]
    public void BuildRequestOptions_CustomDefaultMaxTokens_UsedWhenOptionsNull()
    {
        var result = OllamaLlmClient.BuildRequestOptions(null, defaultMaxTokens: 8192);

        result.NumPredict.Should().Be(8192);
    }

    [Fact]
    public void BuildRequestOptions_CustomDefaultMaxTokens_OverriddenByLlmOptions()
    {
        var options = new LlmOptions { MaxTokens = 2048 };

        var result = OllamaLlmClient.BuildRequestOptions(options, defaultMaxTokens: 8192);

        result.NumPredict.Should().Be(2048);
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(8192)]
    [InlineData(16384)]
    [InlineData(32768)]
    public void BuildRequestOptions_LargeMaxTokens_AcceptedWithoutClamping(int maxTokens)
    {
        var options = new LlmOptions { MaxTokens = maxTokens };

        var result = OllamaLlmClient.BuildRequestOptions(options);

        result.NumPredict.Should().Be(maxTokens);
    }

    [Fact]
    public void BuildRequestOptions_DefaultLlmOptions_UsesConfigDefaultNotHardcoded()
    {
        // When LlmOptions has its default MaxTokens (4096) and a custom
        // defaultMaxTokens is passed, LlmOptions value takes precedence
        var options = new LlmOptions(); // MaxTokens = 4096

        var result = OllamaLlmClient.BuildRequestOptions(options, defaultMaxTokens: 2048);

        result.NumPredict.Should().Be(4096, "LlmOptions.MaxTokens should take precedence over defaultMaxTokens");
    }

    [Fact]
    public void LlmOptions_DefaultMaxTokens_Is4096()
    {
        var options = new LlmOptions();

        options.MaxTokens.Should().Be(4096);
    }
}
