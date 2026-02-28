using FabCopilot.Contracts.Enums;
using FabCopilot.RagService.Services.Evaluation;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Evaluation;

/// <summary>
/// Tests for the A/B testing framework for RAG pipeline comparison.
/// </summary>
public class AbTestManagerTests
{
    private readonly AbTestManager _manager = new(NullLogger<AbTestManager>.Instance);

    [Fact]
    public void CreateExperiment_StoresExperiment()
    {
        var experiment = _manager.CreateExperiment(
            "test-1", "Naive vs Advanced",
            RagPipelineMode.Naive, RagPipelineMode.Advanced, 0.3);

        experiment.ExperimentId.Should().Be("test-1");
        experiment.ControlPipeline.Should().Be(RagPipelineMode.Naive);
        experiment.VariantPipeline.Should().Be(RagPipelineMode.Advanced);
        experiment.VariantPercentage.Should().Be(0.3);
        experiment.IsActive.Should().BeTrue();
    }

    [Fact]
    public void CreateExperiment_ClampsPercentage()
    {
        var exp1 = _manager.CreateExperiment("t1", "", RagPipelineMode.Naive, RagPipelineMode.Advanced, -0.5);
        exp1.VariantPercentage.Should().Be(0.01);

        var exp2 = _manager.CreateExperiment("t2", "", RagPipelineMode.Naive, RagPipelineMode.Advanced, 1.5);
        exp2.VariantPercentage.Should().Be(0.99);
    }

    [Fact]
    public void RouteQuery_ReturnsNull_WhenNoExperiments()
    {
        var result = _manager.RouteQuery("conv-1", RagPipelineMode.Naive);
        result.Should().BeNull();
    }

    [Fact]
    public void RouteQuery_ReturnsResult_WhenExperimentActive()
    {
        _manager.CreateExperiment("test-1", "", RagPipelineMode.Naive, RagPipelineMode.Advanced, 0.5);

        var result = _manager.RouteQuery("conv-1", RagPipelineMode.Naive);
        result.Should().NotBeNull();
        result!.Value.ExperimentId.Should().Be("test-1");
        result.Value.Group.Should().BeOneOf("control", "variant");
    }

    [Fact]
    public void RouteQuery_ConsistentForSameConversation()
    {
        _manager.CreateExperiment("test-1", "", RagPipelineMode.Naive, RagPipelineMode.Advanced, 0.5);

        var result1 = _manager.RouteQuery("conv-1", RagPipelineMode.Naive);
        var result2 = _manager.RouteQuery("conv-1", RagPipelineMode.Naive);

        // Same conversationId should always be routed to the same group
        result1!.Value.Group.Should().Be(result2!.Value.Group);
    }

    [Fact]
    public void RouteQuery_DifferentConversations_SpreadAcrossGroups()
    {
        _manager.CreateExperiment("test-1", "", RagPipelineMode.Naive, RagPipelineMode.Advanced, 0.5);

        var groups = new HashSet<string>();
        for (var i = 0; i < 100; i++)
        {
            var result = _manager.RouteQuery($"conv-{i}", RagPipelineMode.Naive);
            if (result != null) groups.Add(result.Value.Group);
        }

        // With 50% split and 100 conversations, both groups should be represented
        groups.Should().Contain("control");
        groups.Should().Contain("variant");
    }

    [Fact]
    public void RouteQuery_IgnoresInactiveExperiments()
    {
        _manager.CreateExperiment("test-1", "", RagPipelineMode.Naive, RagPipelineMode.Advanced, 0.5);
        _manager.StopExperiment("test-1");

        var result = _manager.RouteQuery("conv-1", RagPipelineMode.Naive);
        result.Should().BeNull();
    }

    [Fact]
    public void RouteQuery_IgnoresMismatchedPipeline()
    {
        // Experiment is for Naive control, but query uses Advanced
        _manager.CreateExperiment("test-1", "", RagPipelineMode.Naive, RagPipelineMode.Advanced, 0.5);

        var result = _manager.RouteQuery("conv-1", RagPipelineMode.Graph);
        result.Should().BeNull();
    }

    [Fact]
    public void RecordResult_StoresMetrics()
    {
        _manager.CreateExperiment("test-1", "", RagPipelineMode.Naive, RagPipelineMode.Advanced, 0.5);

        _manager.RecordResult("test-1", "control", "conv-1", 100.5, 5, 0.85);
        _manager.RecordResult("test-1", "variant", "conv-2", 200.3, 8, 0.92);

        var report = _manager.GetReport("test-1");
        report.Should().NotBeNull();
        report!.ControlMetrics.QueryCount.Should().Be(1);
        report.VariantMetrics.QueryCount.Should().Be(1);
    }

    [Fact]
    public void StopExperiment_ReturnsReport()
    {
        _manager.CreateExperiment("test-1", "Test desc", RagPipelineMode.Naive, RagPipelineMode.Advanced, 0.3);

        _manager.RecordResult("test-1", "control", "c1", 100, 5, 0.8);
        _manager.RecordResult("test-1", "control", "c2", 120, 4, 0.75);
        _manager.RecordResult("test-1", "variant", "v1", 250, 8, 0.9);

        var report = _manager.StopExperiment("test-1");

        report.Should().NotBeNull();
        report!.ExperimentId.Should().Be("test-1");
        report.IsActive.Should().BeFalse();
        report.ControlMetrics.QueryCount.Should().Be(2);
        report.ControlMetrics.AvgSearchDurationMs.Should().Be(110);
        report.VariantMetrics.QueryCount.Should().Be(1);
        report.VariantMetrics.AvgSearchDurationMs.Should().Be(250);
    }

    [Fact]
    public void StopExperiment_NonExistent_ReturnsNull()
    {
        _manager.StopExperiment("non-existent").Should().BeNull();
    }

    [Fact]
    public void GetActiveExperiments_ReturnsOnlyActive()
    {
        _manager.CreateExperiment("active-1", "", RagPipelineMode.Naive, RagPipelineMode.Advanced, 0.2);
        _manager.CreateExperiment("active-2", "", RagPipelineMode.Naive, RagPipelineMode.Graph, 0.3);
        _manager.StopExperiment("active-2");

        var active = _manager.GetActiveExperiments();
        active.Should().HaveCount(1);
        active[0].ExperimentId.Should().Be("active-1");
    }

    [Fact]
    public void RecordFeedback_UpdatesExistingResult()
    {
        _manager.CreateExperiment("test-1", "", RagPipelineMode.Naive, RagPipelineMode.Advanced, 0.5);
        _manager.RecordResult("test-1", "control", "conv-1", 100, 5, 0.8, true);

        _manager.RecordFeedback("test-1", "conv-1", false);

        var report = _manager.GetReport("test-1");
        report!.ControlMetrics.PositiveFeedbackRate.Should().Be(0.0);
    }

    [Fact]
    public void GroupMetrics_ComputesPercentiles()
    {
        _manager.CreateExperiment("test-1", "", RagPipelineMode.Naive, RagPipelineMode.Advanced, 0.5);

        // Record 10 control results with known durations
        for (var i = 1; i <= 10; i++)
        {
            _manager.RecordResult("test-1", "control", $"conv-{i}", i * 100, 5, 0.8);
        }

        var report = _manager.GetReport("test-1");
        report!.ControlMetrics.QueryCount.Should().Be(10);
        report.ControlMetrics.AvgSearchDurationMs.Should().Be(550); // (100+200+...+1000)/10
        report.ControlMetrics.P50SearchDurationMs.Should().BeGreaterThan(0);
        report.ControlMetrics.P95SearchDurationMs.Should().BeGreaterOrEqualTo(report.ControlMetrics.P50SearchDurationMs);
    }
}
