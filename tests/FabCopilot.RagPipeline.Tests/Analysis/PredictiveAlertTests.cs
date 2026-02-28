using FabCopilot.Contracts.Models;
using FabCopilot.McpLogServer.Analysis;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Analysis;

/// <summary>
/// Tests for Predictive Alert generation and management.
/// </summary>
public class PredictiveAlertTests
{
    private static DiagnosticReport CreateTestReport(double confidence = 0.8)
    {
        return new DiagnosticReport
        {
            EquipmentId = "CMP-01",
            Hypotheses =
            [
                new DiagnosticHypothesis
                {
                    Id = "hyp-1", Hypothesis = "슬러리 라인 막힘",
                    Confidence = confidence, Rank = 1,
                    RecommendedActions = ["필터 교체", "라인 퍼지"],
                    ManualReferences = ["[MNL-CMP-Ch5]"]
                }
            ]
        };
    }

    // ── Alert Generation ─────────────────────────────────────────────

    [Fact]
    public void GenerateAlerts_HighConfidence_CreatesCriticalAlert()
    {
        var gen = new PredictiveAlertGenerator();
        var report = CreateTestReport(0.80);

        var alerts = gen.GenerateAlerts(report);

        alerts.Should().HaveCount(1);
        alerts[0].Severity.Should().Be(AlertSeverity.Critical);
        alerts[0].EquipmentId.Should().Be("CMP-01");
        alerts[0].Title.Should().Contain("CRITICAL");
    }

    [Fact]
    public void GenerateAlerts_MediumConfidence_CreatesWarningAlert()
    {
        var gen = new PredictiveAlertGenerator();
        var report = CreateTestReport(0.50);

        var alerts = gen.GenerateAlerts(report);

        alerts.Should().HaveCount(1);
        alerts[0].Severity.Should().Be(AlertSeverity.Warning);
        alerts[0].Title.Should().Contain("WARNING");
    }

    [Fact]
    public void GenerateAlerts_LowConfidence_NoAlert()
    {
        var gen = new PredictiveAlertGenerator();
        var report = CreateTestReport(0.20);

        var alerts = gen.GenerateAlerts(report);

        alerts.Should().BeEmpty("confidence below warning threshold");
    }

    [Fact]
    public void GenerateAlerts_IncludesActionsAndReferences()
    {
        var gen = new PredictiveAlertGenerator();
        var report = CreateTestReport(0.80);

        var alerts = gen.GenerateAlerts(report);

        alerts[0].Actions.Should().Contain("필터 교체");
        alerts[0].ManualReferences.Should().Contain("[MNL-CMP-Ch5]");
        alerts[0].Hypothesis.Should().NotBeNull();
    }

    // ── Suppression ──────────────────────────────────────────────────

    [Fact]
    public void GenerateAlerts_SameIssue_SuppressedWithinWindow()
    {
        var gen = new PredictiveAlertGenerator { SuppressionWindow = TimeSpan.FromHours(1) };
        var report = CreateTestReport(0.80);

        var first = gen.GenerateAlerts(report);
        var second = gen.GenerateAlerts(report);

        first.Should().HaveCount(1);
        second.Should().BeEmpty("same issue should be suppressed within window");
    }

    // ── Acknowledgment ───────────────────────────────────────────────

    [Fact]
    public void Acknowledge_SetsAcknowledgedFields()
    {
        var gen = new PredictiveAlertGenerator();
        gen.GenerateAlerts(CreateTestReport(0.80));

        var activeAlerts = gen.GetActiveAlerts();
        activeAlerts.Should().HaveCount(1);

        var result = gen.Acknowledge(activeAlerts[0].Id, "engineer-1");

        result.Should().BeTrue();
        gen.GetActiveAlerts().Should().BeEmpty();
    }

    [Fact]
    public void Acknowledge_InvalidId_ReturnsFalse()
    {
        var gen = new PredictiveAlertGenerator();

        gen.Acknowledge("INVALID", "user").Should().BeFalse();
    }

    // ── Query Methods ────────────────────────────────────────────────

    [Fact]
    public void GetActiveAlerts_FiltersAcknowledged()
    {
        var gen = new PredictiveAlertGenerator { SuppressionWindow = TimeSpan.Zero };
        gen.GenerateAlerts(CreateTestReport(0.80));

        var secondReport = new DiagnosticReport
        {
            EquipmentId = "CMP-02",
            Hypotheses = [new DiagnosticHypothesis
            {
                Id = "hyp-2", Hypothesis = "다른 문제", Confidence = 0.60, Rank = 1
            }]
        };
        gen.GenerateAlerts(secondReport);

        gen.GetActiveAlerts().Should().HaveCount(2);
        gen.GetActiveAlerts("CMP-01").Should().HaveCount(1);
    }

    [Fact]
    public void GetAlertHistory_IncludesAll()
    {
        var gen = new PredictiveAlertGenerator();
        gen.GenerateAlerts(CreateTestReport(0.80));

        var alerts = gen.GetActiveAlerts();
        gen.Acknowledge(alerts[0].Id, "user");

        gen.GetAlertHistory().Should().HaveCount(1);
        gen.GetActiveAlerts().Should().BeEmpty();
    }

    [Fact]
    public void GetAlertCounts_GroupsBySeverity()
    {
        var gen = new PredictiveAlertGenerator { SuppressionWindow = TimeSpan.Zero };

        gen.GenerateAlerts(CreateTestReport(0.80)); // Critical

        var report2 = new DiagnosticReport
        {
            EquipmentId = "CMP-02",
            Hypotheses = [new DiagnosticHypothesis
            {
                Id = "hyp-2", Hypothesis = "Warning issue", Confidence = 0.50, Rank = 1
            }]
        };
        gen.GenerateAlerts(report2); // Warning

        var counts = gen.GetAlertCounts();

        counts.Should().ContainKey(AlertSeverity.Critical);
        counts.Should().ContainKey(AlertSeverity.Warning);
    }

    // ── Alert Message Format ─────────────────────────────────────────

    [Fact]
    public void GenerateAlerts_MessageContainsEquipmentId()
    {
        var gen = new PredictiveAlertGenerator();
        var alerts = gen.GenerateAlerts(CreateTestReport(0.80));

        alerts[0].Message.Should().Contain("CMP-01");
    }

    [Fact]
    public void GenerateAlerts_AlertIdFormat()
    {
        var gen = new PredictiveAlertGenerator();
        var alerts = gen.GenerateAlerts(CreateTestReport(0.80));

        alerts[0].Id.Should().StartWith("PA-");
    }
}
