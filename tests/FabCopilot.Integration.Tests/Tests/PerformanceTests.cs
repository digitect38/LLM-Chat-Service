using FabCopilot.Integration.Tests.Infrastructure;
using FluentAssertions;

namespace FabCopilot.Integration.Tests.Tests;

[Collection("FabCopilot Services")]
[Trait("Category", "Integration")]
public class PerformanceTests
{
    private readonly FabCopilotServiceFixture _fixture;

    public PerformanceTests(FabCopilotServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task TimeToFirstToken_ShouldBeLessThan60Seconds()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);

        var response = await _fixture.Client.SendAndCollectAsync(
            "CMP 패드 교체 시기는?");

        response.Error.Should().BeNull();
        response.TimeToFirstToken.Should().BeLessThan(TimeSpan.FromSeconds(60),
            "the first token should arrive within 60 seconds (includes RAG retrieval + LLM inference start)");
    }

    [SkippableFact]
    public async Task TotalResponseTime_ShouldBeLessThan180Seconds()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);

        var response = await _fixture.Client.SendAndCollectAsync(
            "웨이퍼 스크래치 발생 원인과 해결방법",
            timeout: TimeSpan.FromSeconds(180));

        response.Error.Should().BeNull();
        response.FullText.Should().NotBeNullOrWhiteSpace();
        response.TotalTime.Should().BeLessThan(TimeSpan.FromSeconds(180),
            "the complete response should finish within 180 seconds");
    }
}
