using FluentAssertions;
using FabCopilot.Contracts.Enums;
using FabCopilot.RagService.Services;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class QueryRouterEdgeCaseTests
{
    [Fact]
    public void Classify_ErrorPriorityOverProcedure()
    {
        // "알람 교체 절차" contains both Error ("알람") and Procedure ("교체", "절차") keywords
        // Error has higher priority in the rules array
        var result = QueryRouter.Classify("알람 교체 절차");
        result.Should().Be(QueryIntent.Error);
    }

    [Fact]
    public void Classify_ProcedurePriorityOverPart()
    {
        // "패드 교체 방법" contains both Part ("패드") and Procedure ("교체", "방법") keywords
        // Procedure has higher priority than Part in the rules array
        var result = QueryRouter.Classify("패드 교체 방법");
        result.Should().Be(QueryIntent.Procedure);
    }

    [Fact]
    public void Classify_AlarmCode_A100_MatchesError()
    {
        var result = QueryRouter.Classify("A100");
        result.Should().Be(QueryIntent.Error);
    }

    [Fact]
    public void Classify_AlarmCode_E001_MatchesError()
    {
        var result = QueryRouter.Classify("E001 code meaning");
        result.Should().Be(QueryIntent.Error);
    }

    [Fact]
    public void Classify_WhitespaceOnly_ReturnsGeneral()
    {
        var result = QueryRouter.Classify("   ");
        result.Should().Be(QueryIntent.General);
    }

    [Fact]
    public void Classify_VeryLongQuery_StillClassifies()
    {
        var longQuery = new string('x', 1000) + " alarm issue detected";
        var result = QueryRouter.Classify(longQuery);
        result.Should().Be(QueryIntent.Error);
    }

    [Fact]
    public void GetPreferredDocTypes_Part_ContainsMaintenanceAndParts()
    {
        var types = QueryRouter.GetPreferredDocTypes(QueryIntent.Part);
        types.Should().NotBeNull();
        types.Should().Contain("maintenance");
        types.Should().Contain("parts");
        types.Should().Contain("consumable");
    }

    [Fact]
    public void GetPreferredDocTypes_Definition_ContainsOverview()
    {
        var types = QueryRouter.GetPreferredDocTypes(QueryIntent.Definition);
        types.Should().NotBeNull();
        types.Should().Contain("overview");
        types.Should().Contain("glossary");
    }

    [Fact]
    public void GetPreferredDocTypes_Spec_ContainsSpecification()
    {
        var types = QueryRouter.GetPreferredDocTypes(QueryIntent.Spec);
        types.Should().NotBeNull();
        types.Should().Contain("specification");
        types.Should().Contain("parameter");
    }

    [Fact]
    public void GetPreferredDocTypes_General_ReturnsNull()
    {
        var types = QueryRouter.GetPreferredDocTypes(QueryIntent.General);
        types.Should().BeNull();
    }
}
