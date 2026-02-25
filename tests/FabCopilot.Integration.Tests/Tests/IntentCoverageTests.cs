using FabCopilot.Integration.Tests.Infrastructure;
using FluentAssertions;

namespace FabCopilot.Integration.Tests.Tests;

[Collection("FabCopilot Services")]
[Trait("Category", "Integration")]
public class IntentCoverageTests
{
    private readonly FabCopilotServiceFixture _fixture;

    public IntentCoverageTests(FabCopilotServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task ProcedureIntent_ShouldContainStepsOrChecklist()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);

        var response = await _fixture.Client.SendAndCollectAsync(
            "CMP 장비의 일일 점검 항목은?");

        response.Error.Should().BeNull();
        response.FullText.Should().NotBeNullOrWhiteSpace();

        var text = response.FullText;
        var procedureIndicators = new[]
        {
            "점검", "확인", "절차", "순서", "단계",
            "1.", "2.", "체크", "항목",
            "- ", "* "
        };

        var matchCount = procedureIndicators
            .Count(indicator => text.Contains(indicator, StringComparison.OrdinalIgnoreCase));

        matchCount.Should().BeGreaterOrEqualTo(2,
            "a Procedure-intent response should contain steps, checklists, or procedural language");
    }

    [SkippableFact]
    public async Task TroubleshootingIntent_ShouldContainCauseAndSolution()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);

        var response = await _fixture.Client.SendAndCollectAsync(
            "웨이퍼 스크래치 발생 원인과 해결방법");

        response.Error.Should().BeNull();
        response.FullText.Should().NotBeNullOrWhiteSpace();

        var text = response.FullText;
        var causeIndicators = new[] { "원인", "발생", "때문", "인해", "cause" };
        var solutionIndicators = new[] { "해결", "조치", "방법", "대처", "교체", "수정", "solution" };

        var hasCause = causeIndicators
            .Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
        var hasSolution = solutionIndicators
            .Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

        hasCause.Should().BeTrue(
            "a Troubleshooting response should mention causes");
        hasSolution.Should().BeTrue(
            "a Troubleshooting response should mention solutions or remediation steps");
    }
}
