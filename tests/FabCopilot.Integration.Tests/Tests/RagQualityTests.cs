using FabCopilot.Integration.Tests.Infrastructure;
using FabCopilot.Integration.Tests.TestData;
using FluentAssertions;

namespace FabCopilot.Integration.Tests.Tests;

[Collection("FabCopilot Services")]
[Trait("Category", "Integration")]
public class RagQualityTests
{
    private readonly FabCopilotServiceFixture _fixture;

    public RagQualityTests(FabCopilotServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task Q1_PadReplacement_ContainsDomainKeywords()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);
        await AssertContainsAnyKeyword(BenchmarkQueries.Q1);
    }

    [SkippableFact]
    public async Task Q2_SlurryPressureAlarm_ContainsDomainKeywords()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);
        await AssertContainsAnyKeyword(BenchmarkQueries.Q2);
    }

    [SkippableFact]
    public async Task Q3_WaferScratch_ContainsDomainKeywords()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);
        await AssertContainsAnyKeyword(BenchmarkQueries.Q3);
    }

    [SkippableFact]
    public async Task Q4_DailyInspection_ContainsDomainKeywords()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);
        await AssertContainsAnyKeyword(BenchmarkQueries.Q4);
    }

    [SkippableFact]
    public async Task Q5_LowMrr_ContainsDomainKeywords()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);
        await AssertContainsAnyKeyword(BenchmarkQueries.Q5);
    }

    private async Task AssertContainsAnyKeyword(BenchmarkQuery query)
    {
        var response = await _fixture.Client.SendAndCollectAsync(query.Text);

        response.Error.Should().BeNull(
            $"query {query.Id} should not produce an error");
        response.FullText.Should().NotBeNullOrWhiteSpace(
            $"query {query.Id} should produce a response");

        var text = response.FullText;
        var matchedKeywords = query.ExpectedKeywords
            .Where(kw => text.Contains(kw, StringComparison.OrdinalIgnoreCase))
            .ToList();

        matchedKeywords.Should().NotBeEmpty(
            $"query {query.Id} response should contain at least one expected keyword " +
            $"[{string.Join(", ", query.ExpectedKeywords)}] but got: " +
            $"{text[..Math.Min(text.Length, 200)]}...");
    }
}
