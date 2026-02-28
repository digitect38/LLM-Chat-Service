using FabCopilot.Contracts.Interfaces;
using FabCopilot.McpLogServer.Analysis;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Analysis;

/// <summary>
/// Tests for the Alarm/Event Pattern Analysis Engine.
/// </summary>
public class AlarmPatternAnalyzerTests
{
    private static List<AlarmEvent> CreateAlarms(params (string Code, int HoursAgo, int? ClearedMinutesLater)[] specs)
    {
        var now = DateTimeOffset.UtcNow;
        return specs.Select(s => new AlarmEvent
        {
            EquipmentId = "CMP-01",
            AlarmCode = s.Code,
            Description = $"Alarm {s.Code}",
            Severity = "Warning",
            Timestamp = now.AddHours(-s.HoursAgo),
            ClearedAt = s.ClearedMinutesLater.HasValue
                ? now.AddHours(-s.HoursAgo).AddMinutes(s.ClearedMinutesLater.Value)
                : null
        }).ToList();
    }

    // ── Top-N Frequent ───────────────────────────────────────────────

    [Fact]
    public void TopNFrequent_ReturnsCorrectCounts()
    {
        var alarms = CreateAlarms(
            ("A100", 10, null), ("A100", 8, null), ("A100", 5, null),
            ("A201", 6, null), ("A201", 3, null),
            ("A305", 1, null));

        var topN = AlarmPatternAnalyzer.TopNFrequent(alarms, 2);

        topN.Should().HaveCount(2);
        topN[0].AlarmCode.Should().Be("A100");
        topN[0].Count.Should().Be(3);
        topN[1].AlarmCode.Should().Be("A201");
        topN[1].Count.Should().Be(2);
    }

    [Fact]
    public void TopNFrequent_EmptyList_ReturnsEmpty()
    {
        AlarmPatternAnalyzer.TopNFrequent([], 5).Should().BeEmpty();
    }

    // ── Hourly Distribution ──────────────────────────────────────────

    [Fact]
    public void HourlyDistribution_CoversAll24Hours()
    {
        var alarms = CreateAlarms(("A100", 1, null));
        var dist = AlarmPatternAnalyzer.HourlyDistribution(alarms);

        dist.Should().HaveCount(24);
        dist.Values.Sum().Should().Be(1);
    }

    // ── Day of Week Distribution ─────────────────────────────────────

    [Fact]
    public void DayOfWeekDistribution_CoversAllDays()
    {
        var alarms = CreateAlarms(("A100", 1, null));
        var dist = AlarmPatternAnalyzer.DayOfWeekDistribution(alarms);

        dist.Should().HaveCount(7);
    }

    // ── Cascading Patterns ───────────────────────────────────────────

    [Fact]
    public void DetectCascadingPatterns_DetectsSequence()
    {
        var now = DateTimeOffset.UtcNow;
        var alarms = new List<AlarmEvent>
        {
            // First sequence: A100 → A201 → A305
            new() { AlarmCode = "A100", Timestamp = now.AddMinutes(-60), EquipmentId = "CMP-01" },
            new() { AlarmCode = "A201", Timestamp = now.AddMinutes(-55), EquipmentId = "CMP-01" },
            new() { AlarmCode = "A305", Timestamp = now.AddMinutes(-50), EquipmentId = "CMP-01" },
            // Second sequence (same pattern)
            new() { AlarmCode = "A100", Timestamp = now.AddMinutes(-30), EquipmentId = "CMP-01" },
            new() { AlarmCode = "A201", Timestamp = now.AddMinutes(-25), EquipmentId = "CMP-01" },
            new() { AlarmCode = "A305", Timestamp = now.AddMinutes(-20), EquipmentId = "CMP-01" }
        };

        var patterns = AlarmPatternAnalyzer.DetectCascadingPatterns(alarms, TimeSpan.FromMinutes(10), minOccurrences: 2);

        patterns.Should().NotBeEmpty();
        patterns.Should().Contain(p => p.Pattern == "A100 → A201");
    }

    [Fact]
    public void DetectCascadingPatterns_NoPatterns_WhenGapTooBig()
    {
        var now = DateTimeOffset.UtcNow;
        var alarms = new List<AlarmEvent>
        {
            new() { AlarmCode = "A100", Timestamp = now.AddHours(-10), EquipmentId = "CMP-01" },
            new() { AlarmCode = "A201", Timestamp = now, EquipmentId = "CMP-01" }
        };

        var patterns = AlarmPatternAnalyzer.DetectCascadingPatterns(alarms, TimeSpan.FromMinutes(5));
        patterns.Should().BeEmpty();
    }

    // ── MTBF ─────────────────────────────────────────────────────────

    [Fact]
    public void ComputeMtbf_CalculatesCorrectly()
    {
        var alarms = CreateAlarms(
            ("A100", 48, null), ("A100", 24, null), ("A100", 0, null));

        var mtbf = AlarmPatternAnalyzer.ComputeMtbf(alarms);

        mtbf.Should().ContainKey("A100");
        mtbf["A100"].TotalHours.Should().BeApproximately(24, 1);
    }

    [Fact]
    public void ComputeMtbf_SingleAlarm_NoResult()
    {
        var alarms = CreateAlarms(("A100", 1, null));
        var mtbf = AlarmPatternAnalyzer.ComputeMtbf(alarms);

        mtbf.Should().NotContainKey("A100",
            because: "MTBF requires at least 2 occurrences");
    }

    // ── MTTR ─────────────────────────────────────────────────────────

    [Fact]
    public void ComputeMttr_CalculatesFromClearedAlarms()
    {
        var alarms = CreateAlarms(
            ("A100", 10, 30), // 30 min to clear
            ("A100", 5, 60)); // 60 min to clear

        var mttr = AlarmPatternAnalyzer.ComputeMttr(alarms);

        mttr.Should().ContainKey("A100");
        mttr["A100"].TotalMinutes.Should().BeApproximately(45, 1); // (30 + 60) / 2
    }

    [Fact]
    public void ComputeMttr_IgnoresUnclearedAlarms()
    {
        var alarms = CreateAlarms(
            ("A100", 10, null), // Not cleared
            ("A201", 5, 30));   // Cleared

        var mttr = AlarmPatternAnalyzer.ComputeMttr(alarms);

        mttr.Should().NotContainKey("A100");
        mttr.Should().ContainKey("A201");
    }

    // ── Co-occurring Alarms ──────────────────────────────────────────

    [Fact]
    public void FindCoOccurringAlarms_DetectsPairs()
    {
        var now = DateTimeOffset.UtcNow;
        var alarms = new List<AlarmEvent>
        {
            new() { AlarmCode = "A100", Timestamp = now.AddMinutes(-10), EquipmentId = "CMP-01" },
            new() { AlarmCode = "A201", Timestamp = now.AddMinutes(-8), EquipmentId = "CMP-01" },
            new() { AlarmCode = "A100", Timestamp = now.AddMinutes(-5), EquipmentId = "CMP-01" },
            new() { AlarmCode = "A201", Timestamp = now.AddMinutes(-3), EquipmentId = "CMP-01" }
        };

        var coOccurring = AlarmPatternAnalyzer.FindCoOccurringAlarms(alarms, TimeSpan.FromMinutes(30));

        coOccurring.Should().NotBeEmpty();
        coOccurring.Should().Contain(c => c.AlarmA == "A100" && c.AlarmB == "A201" ||
                                          c.AlarmA == "A201" && c.AlarmB == "A100");
    }

    [Fact]
    public void FindCoOccurringAlarms_EmptyList_ReturnsEmpty()
    {
        AlarmPatternAnalyzer.FindCoOccurringAlarms([]).Should().BeEmpty();
    }
}
