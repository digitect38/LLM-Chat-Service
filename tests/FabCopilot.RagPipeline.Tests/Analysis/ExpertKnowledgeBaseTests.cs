using FabCopilot.Contracts.Models;
using FabCopilot.McpLogServer.Analysis;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Analysis;

/// <summary>
/// Tests for Expert Knowledge Base (Tier 2 diagnostic reasoning).
/// </summary>
public class ExpertKnowledgeBaseTests
{
    private static ExpertRule CreateTestRule(string id = "ER-0001") => new()
    {
        Id = id,
        Name = "Slurry Flow Low",
        EquipmentType = "CMP",
        Triggers =
        [
            new RuleTrigger { Type = "sensor", Source = "slurry_flow", Operator = "lt", Value = 140 },
            new RuleTrigger { Type = "alarm", Source = "A201" }
        ],
        Hypothesis = "슬러리 라인 막힘",
        RootCause = "Clogged slurry filter",
        Actions = ["필터 세척 또는 교체", "슬러리 라인 퍼지"],
        Confidence = 0.80,
        ManualReference = "[MNL-CMP-Ch5-S2.1-{Line:45-67}]"
    };

    // ── CRUD ─────────────────────────────────────────────────────────

    [Fact]
    public void AddRule_AssignsIdAndStores()
    {
        var kb = new ExpertKnowledgeBase();
        var rule = new ExpertRule
        {
            Name = "Test Rule", EquipmentType = "CMP",
            Hypothesis = "Test", Actions = ["Action 1"]
        };

        var added = kb.AddRule(rule);

        added.Id.Should().StartWith("ER-");
        kb.GetRule(added.Id).Should().NotBeNull();
    }

    [Fact]
    public void AddRule_WithExistingId_UsesProvidedId()
    {
        var kb = new ExpertKnowledgeBase();
        var rule = CreateTestRule("ER-0042");
        kb.AddRule(rule);

        kb.GetRule("ER-0042").Should().NotBeNull();
    }

    [Fact]
    public void UpdateRule_IncrementsVersion()
    {
        var kb = new ExpertKnowledgeBase();
        kb.AddRule(CreateTestRule());

        var updated = kb.UpdateRule("ER-0001", r => r.Confidence = 0.95);

        updated.Should().NotBeNull();
        updated!.Version.Should().Be(2);
        updated.Confidence.Should().Be(0.95);
    }

    [Fact]
    public void RemoveRule_DeletesFromKb()
    {
        var kb = new ExpertKnowledgeBase();
        kb.AddRule(CreateTestRule());

        kb.RemoveRule("ER-0001").Should().BeTrue();
        kb.GetRule("ER-0001").Should().BeNull();
    }

    [Fact]
    public void ListRules_FiltersByEquipmentType()
    {
        var kb = new ExpertKnowledgeBase();
        kb.AddRule(CreateTestRule());
        kb.AddRule(new ExpertRule { Id = "ER-0002", Name = "Etch Rule", EquipmentType = "ETCH" });

        kb.ListRules("CMP").Should().HaveCount(1);
        kb.ListRules("ETCH").Should().HaveCount(1);
    }

    [Fact]
    public void ListRules_FiltersActiveOnly()
    {
        var kb = new ExpertKnowledgeBase();
        kb.AddRule(CreateTestRule());
        kb.AddRule(new ExpertRule { Id = "ER-0002", Name = "Inactive", EquipmentType = "CMP", IsActive = false });

        kb.ListRules(activeOnly: true).Should().HaveCount(1);
        kb.ListRules(activeOnly: false).Should().HaveCount(1);
    }

    // ── Rule Evaluation ──────────────────────────────────────────────

    [Fact]
    public void EvaluateRules_MatchingSensorTrigger_ReturnsMatch()
    {
        var kb = new ExpertKnowledgeBase();
        kb.AddRule(CreateTestRule());

        var matches = kb.EvaluateRules("CMP",
            sensorValues: new Dictionary<string, double> { ["slurry_flow"] = 130 }, // Below 140
            activeAlarms: new HashSet<string> { "A201" });

        matches.Should().HaveCount(1);
        matches[0].MatchedTriggers.Should().Be(2);
        matches[0].TotalTriggers.Should().Be(2);
        matches[0].MatchRatio.Should().Be(1.0);
    }

    [Fact]
    public void EvaluateRules_PartialMatch_ReturnsPartialRatio()
    {
        var kb = new ExpertKnowledgeBase();
        kb.AddRule(CreateTestRule());

        var matches = kb.EvaluateRules("CMP",
            sensorValues: new Dictionary<string, double> { ["slurry_flow"] = 130 });
        // Only sensor trigger matches, alarm A201 is not active

        matches.Should().HaveCount(1);
        matches[0].MatchedTriggers.Should().Be(1);
        matches[0].MatchRatio.Should().Be(0.5);
    }

    [Fact]
    public void EvaluateRules_NoMatch_ReturnsEmpty()
    {
        var kb = new ExpertKnowledgeBase();
        kb.AddRule(CreateTestRule());

        var matches = kb.EvaluateRules("CMP",
            sensorValues: new Dictionary<string, double> { ["slurry_flow"] = 180 }); // Above threshold

        matches.Should().BeEmpty();
    }

    [Fact]
    public void EvaluateRules_InactiveRule_NotMatched()
    {
        var kb = new ExpertKnowledgeBase();
        var rule = CreateTestRule();
        rule.IsActive = false;
        kb.AddRule(rule);

        var matches = kb.EvaluateRules("CMP",
            sensorValues: new Dictionary<string, double> { ["slurry_flow"] = 130 },
            activeAlarms: new HashSet<string> { "A201" });

        matches.Should().BeEmpty();
    }

    [Fact]
    public void EvaluateRules_WrongEquipmentType_NotMatched()
    {
        var kb = new ExpertKnowledgeBase();
        kb.AddRule(CreateTestRule());

        var matches = kb.EvaluateRules("ETCH",
            sensorValues: new Dictionary<string, double> { ["slurry_flow"] = 130 });

        matches.Should().BeEmpty();
    }

    // ── Feedback & Auto-Deactivation ─────────────────────────────────

    [Fact]
    public void RecordFeedback_PositiveFeedback_IncreasesConfidence()
    {
        var kb = new ExpertKnowledgeBase();
        var rule = CreateTestRule();
        rule.Confidence = 0.5;
        kb.AddRule(rule);

        kb.RecordFeedback("ER-0001", wasCorrect: true);

        var updated = kb.GetRule("ER-0001")!;
        updated.HitCount.Should().Be(1);
        updated.ConfirmCount.Should().Be(1);
        updated.Confidence.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void RecordFeedback_AutoDeactivation_WhenPrecisionBelowThreshold()
    {
        var kb = new ExpertKnowledgeBase();
        kb.MinHitsForDeactivation = 5;
        kb.MinPrecisionThreshold = 0.70;

        var rule = CreateTestRule();
        rule.Confidence = 0.3;
        kb.AddRule(rule);

        // 5 hits, 2 correct = 40% precision < 70%
        for (var i = 0; i < 3; i++) kb.RecordFeedback("ER-0001", false);
        for (var i = 0; i < 2; i++) kb.RecordFeedback("ER-0001", true);

        kb.GetRule("ER-0001")!.IsActive.Should().BeFalse("precision 40% < 70% threshold");
    }

    // ── Stats ────────────────────────────────────────────────────────

    [Fact]
    public void GetStats_ReturnsCorrectCounts()
    {
        var kb = new ExpertKnowledgeBase();
        kb.AddRule(CreateTestRule());
        kb.AddRule(new ExpertRule { Id = "ER-0002", Name = "R2", EquipmentType = "CMP", IsActive = false });

        var stats = kb.GetStats();

        stats.TotalRules.Should().Be(2);
        stats.ActiveRules.Should().Be(1);
        stats.InactiveRules.Should().Be(1);
    }

    // ── Trigger Operator Tests ───────────────────────────────────────

    [Theory]
    [InlineData("gt", 50, 60, true)]
    [InlineData("gt", 50, 40, false)]
    [InlineData("lt", 50, 40, true)]
    [InlineData("lt", 50, 60, false)]
    [InlineData("gte", 50, 50, true)]
    [InlineData("lte", 50, 50, true)]
    public void EvaluateRules_OperatorVariants(string op, double threshold, double value, bool shouldMatch)
    {
        var kb = new ExpertKnowledgeBase();
        kb.AddRule(new ExpertRule
        {
            Id = "ER-0099", Name = "Op Test", EquipmentType = "CMP",
            Triggers = [new RuleTrigger { Type = "sensor", Source = "test", Operator = op, Value = threshold }],
            Hypothesis = "test"
        });

        var matches = kb.EvaluateRules("CMP",
            sensorValues: new Dictionary<string, double> { ["test"] = value });

        if (shouldMatch)
            matches.Should().HaveCount(1);
        else
            matches.Should().BeEmpty();
    }
}
