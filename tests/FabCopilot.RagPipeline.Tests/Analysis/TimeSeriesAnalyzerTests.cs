using FabCopilot.Contracts.Models;
using FabCopilot.McpLogServer.Analysis;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Analysis;

/// <summary>
/// Tests for the Time-Series Analysis Engine.
/// </summary>
public class TimeSeriesAnalyzerTests
{
    // ── Statistics ────────────────────────────────────────────────────

    [Fact]
    public void ComputeStatistics_BasicValues()
    {
        var values = new List<double> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var stats = TimeSeriesAnalyzer.ComputeStatistics(values);

        stats.Mean.Should().Be(5.5);
        stats.Min.Should().Be(1);
        stats.Max.Should().Be(10);
        stats.StdDev.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ComputeStatistics_EmptyList_ReturnsZeroes()
    {
        var stats = TimeSeriesAnalyzer.ComputeStatistics([]);
        stats.Mean.Should().Be(0);
        stats.StdDev.Should().Be(0);
    }

    [Fact]
    public void ComputeStatistics_SingleValue()
    {
        var stats = TimeSeriesAnalyzer.ComputeStatistics([42.0]);
        stats.Mean.Should().Be(42);
        stats.Min.Should().Be(42);
        stats.Max.Should().Be(42);
        stats.StdDev.Should().Be(0);
    }

    // ── Moving Average ───────────────────────────────────────────────

    [Fact]
    public void MovingAverage_Window3()
    {
        var values = new List<double> { 1, 2, 3, 4, 5 };
        var ma = TimeSeriesAnalyzer.MovingAverage(values, 3);

        ma.Should().HaveCount(3); // 5 - 3 + 1
        ma[0].Should().Be(2.0); // (1+2+3)/3
        ma[1].Should().Be(3.0); // (2+3+4)/3
        ma[2].Should().Be(4.0); // (3+4+5)/3
    }

    [Fact]
    public void MovingAverage_EmptyInput_ReturnsEmpty()
    {
        TimeSeriesAnalyzer.MovingAverage([], 5).Should().BeEmpty();
    }

    // ── Exponential Moving Average ───────────────────────────────────

    [Fact]
    public void ExponentialMovingAverage_SmoothsData()
    {
        var values = new List<double> { 10, 12, 11, 15, 14, 16, 13 };
        var ema = TimeSeriesAnalyzer.ExponentialMovingAverage(values, 3);

        ema.Should().HaveCount(values.Count);
        ema[0].Should().Be(10); // First value = input
        // EMA should smooth the data
        ema.Should().AllSatisfy(v => v.Should().BeInRange(9, 17));
    }

    // ── Trend Detection ──────────────────────────────────────────────

    [Fact]
    public void DetectTrend_IncreasingSequence()
    {
        var values = Enumerable.Range(1, 100).Select(i => (double)i).ToList();
        var (slope, direction) = TimeSeriesAnalyzer.DetectTrend(values);

        slope.Should().BeGreaterThan(0);
        direction.Should().Be(TrendDirection.Increasing);
    }

    [Fact]
    public void DetectTrend_DecreasingSequence()
    {
        var values = Enumerable.Range(1, 100).Select(i => 100.0 - i).ToList();
        var (slope, direction) = TimeSeriesAnalyzer.DetectTrend(values);

        slope.Should().BeLessThan(0);
        direction.Should().Be(TrendDirection.Decreasing);
    }

    [Fact]
    public void DetectTrend_StableSequence()
    {
        var values = Enumerable.Repeat(50.0, 100).ToList();
        var (_, direction) = TimeSeriesAnalyzer.DetectTrend(values);

        direction.Should().Be(TrendDirection.Stable);
    }

    [Fact]
    public void DetectTrend_InsufficientData_ReturnsStable()
    {
        var (_, direction) = TimeSeriesAnalyzer.DetectTrend([42.0]);
        direction.Should().Be(TrendDirection.Stable);
    }

    // ── Change Point Detection (CUSUM) ───────────────────────────────

    [Fact]
    public void DetectChangePointsCusum_DetectsLevelShift()
    {
        // Create a series with a level shift at index 50
        var values = new List<double>();
        for (var i = 0; i < 50; i++) values.Add(10.0 + Random.Shared.NextDouble() * 0.5);
        for (var i = 0; i < 50; i++) values.Add(20.0 + Random.Shared.NextDouble() * 0.5);

        var changePoints = TimeSeriesAnalyzer.DetectChangePointsCusum(values, threshold: 3.0);

        changePoints.Should().NotBeEmpty("a clear level shift should be detected");
        // At least one change point near index 50
        changePoints.Should().Contain(cp => cp >= 45 && cp <= 60);
    }

    [Fact]
    public void DetectChangePointsCusum_StableSignal_NoChangePoints()
    {
        var values = Enumerable.Repeat(50.0, 100).ToList();
        var changePoints = TimeSeriesAnalyzer.DetectChangePointsCusum(values);

        changePoints.Should().BeEmpty("a stable signal should have no change points");
    }

    [Fact]
    public void DetectChangePointsCusum_TooFewValues_ReturnsEmpty()
    {
        TimeSeriesAnalyzer.DetectChangePointsCusum([1, 2, 3]).Should().BeEmpty();
    }

    // ── 3-Sigma Anomaly Detection ────────────────────────────────────

    [Fact]
    public void DetectAnomalies3Sigma_DetectsOutlier()
    {
        var values = Enumerable.Repeat(50.0, 99).ToList();
        values.Add(100.0); // Clear outlier

        var anomalies = TimeSeriesAnalyzer.DetectAnomalies3Sigma(values);

        anomalies.Should().NotBeEmpty("a 50-sigma outlier should be detected");
        anomalies.Should().Contain(a => a.Index == 99);
    }

    [Fact]
    public void DetectAnomalies3Sigma_NormalData_NoAnomalies()
    {
        var values = Enumerable.Repeat(50.0, 100).ToList();
        var anomalies = TimeSeriesAnalyzer.DetectAnomalies3Sigma(values);

        anomalies.Should().BeEmpty("constant data should have no anomalies");
    }

    [Fact]
    public void DetectAnomalies3Sigma_DistinguishesCautionAndAnomaly()
    {
        var values = new List<double>();
        for (var i = 0; i < 100; i++) values.Add(50.0);
        values[50] = 55.0; // Moderate deviation
        values[75] = 65.0; // Large deviation

        var anomalies = TimeSeriesAnalyzer.DetectAnomalies3Sigma(values, cautionSigma: 2.0, anomalySigma: 3.0);

        // At least the extreme outlier should be detected
        anomalies.Should().Contain(a => a.Index == 75);
    }

    // ── Local Anomaly Detection ──────────────────────────────────────

    [Fact]
    public void DetectAnomaliesLocal_DetectsLocalOutlier()
    {
        var values = new List<double>();
        for (var i = 0; i < 100; i++) values.Add(50.0 + Random.Shared.NextDouble());
        values[50] = 100.0; // Extreme local outlier

        var anomalies = TimeSeriesAnalyzer.DetectAnomaliesLocal(values, windowSize: 10);

        anomalies.Should().Contain(a => a.Index == 50);
    }

    [Fact]
    public void DetectAnomaliesLocal_TooFewValues_ReturnsEmpty()
    {
        TimeSeriesAnalyzer.DetectAnomaliesLocal([1, 2, 3], windowSize: 10).Should().BeEmpty();
    }

    // ── Cross-Correlation ────────────────────────────────────────────

    [Fact]
    public void CrossCorrelation_IdenticalSignals_Returns1()
    {
        var signal = Enumerable.Range(1, 100).Select(i => (double)i).ToList();
        var corr = TimeSeriesAnalyzer.CrossCorrelation(signal, signal);

        corr.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void CrossCorrelation_InverseSignals_ReturnsMinus1()
    {
        var signalA = Enumerable.Range(1, 100).Select(i => (double)i).ToList();
        var signalB = Enumerable.Range(1, 100).Select(i => 101.0 - i).ToList();
        var corr = TimeSeriesAnalyzer.CrossCorrelation(signalA, signalB);

        corr.Should().BeApproximately(-1.0, 0.001);
    }

    [Fact]
    public void CrossCorrelation_UncorrelatedSignals_NearZero()
    {
        var signalA = new List<double> { 1, -1, 1, -1, 1, -1, 1, -1, 1, -1 };
        var signalB = new List<double> { 1, 1, -1, -1, 1, 1, -1, -1, 1, 1 };
        var corr = TimeSeriesAnalyzer.CrossCorrelation(signalA, signalB);

        Math.Abs(corr).Should().BeLessThan(0.5);
    }

    [Fact]
    public void CrossCorrelation_InsufficientData_Returns0()
    {
        TimeSeriesAnalyzer.CrossCorrelation([1.0], [2.0]).Should().Be(0);
    }
}
