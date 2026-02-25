using FabCopilot.Integration.Tests.Infrastructure;
using FluentAssertions;

namespace FabCopilot.Integration.Tests.Tests;

[Collection("FabCopilot Services")]
[Trait("Category", "Integration")]
public class CitationValidationTests
{
    private readonly FabCopilotServiceFixture _fixture;

    public CitationValidationTests(FabCopilotServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task Citations_ShouldExistForDomainQuery()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);

        var response = await _fixture.Client.SendAndCollectAsync(
            "CMP 패드 교체 시기는?");

        response.Error.Should().BeNull();
        response.Citations.Should().NotBeEmpty(
            "a domain-specific query should return citations from the knowledge base");
    }

    [SkippableFact]
    public async Task Citations_ScoresShouldBeValid()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);

        var response = await _fixture.Client.SendAndCollectAsync(
            "슬러리 공급 압력 이상 시 알람코드와 대처 방법은?");

        response.Error.Should().BeNull();

        foreach (var citation in response.Citations)
        {
            citation.Score.Should().BeGreaterOrEqualTo(0f,
                $"citation '{citation.CitationId}' score should be >= 0");
            citation.Score.Should().BeLessOrEqualTo(1f,
                $"citation '{citation.CitationId}' score should be <= 1");
        }
    }

    [SkippableFact]
    public async Task Citations_FileNamesShouldBeValid()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);

        var response = await _fixture.Client.SendAndCollectAsync(
            "CMP 장비의 일일 점검 항목은?");

        response.Error.Should().BeNull();

        foreach (var citation in response.Citations)
        {
            citation.FileName.Should().NotBeNullOrWhiteSpace(
                $"citation '{citation.CitationId}' should have a file name");
            citation.FileName.Should().ContainAny(
                [".pdf", ".md", ".txt", ".docx"],
                $"citation '{citation.CitationId}' file name should have a known extension");
        }
    }
}
