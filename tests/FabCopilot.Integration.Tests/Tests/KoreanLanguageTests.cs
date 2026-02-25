using FabCopilot.Integration.Tests.Infrastructure;
using FluentAssertions;

namespace FabCopilot.Integration.Tests.Tests;

[Collection("FabCopilot Services")]
[Trait("Category", "Integration")]
public class KoreanLanguageTests
{
    private readonly FabCopilotServiceFixture _fixture;

    public KoreanLanguageTests(FabCopilotServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task KoreanQuery_ShouldReturnKoreanResponse()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);

        var response = await _fixture.Client.SendAndCollectAsync(
            "MRR이 낮아지는 원인은?");

        response.Error.Should().BeNull();
        response.FullText.Should().NotBeNullOrWhiteSpace();

        var text = response.FullText;
        var koreanCharCount = text.Count(c =>
            c >= '\uAC00' && c <= '\uD7A3');  // Hangul syllables
        var totalNonWhitespace = text.Count(c => !char.IsWhiteSpace(c));

        var koreanRatio = totalNonWhitespace > 0
            ? (double)koreanCharCount / totalNonWhitespace
            : 0;

        koreanRatio.Should().BeGreaterThan(0.10,
            "a Korean-language query should produce a response with >10% Korean characters " +
            $"(got {koreanRatio:P1}, {koreanCharCount}/{totalNonWhitespace} chars)");
    }
}
