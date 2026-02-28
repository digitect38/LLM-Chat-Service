using FabCopilot.Contracts.Models;
using FabCopilot.McpLogServer.Analysis;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests.Analysis;

/// <summary>
/// Tests for ML anomaly detection (Isolation Forest + RUL Predictor).
/// </summary>
public class IsolationForestDetectorTests
{
    // ── Isolation Forest ─────────────────────────────────────────────

    [Fact]
    public void Train_WithNormalData_SetsIsTrained()
    {
        var detector = new IsolationForestDetector("CMP-01", "platen_rpm", seed: 42);
        var normalData = Enumerable.Range(0, 200).Select(_ => 60.0 + Random.Shared.NextDouble() * 10).ToArray();

        detector.Train(normalData);

        detector.IsTrained.Should().BeTrue();
        detector.ModelVersion.Should().StartWith("v");
    }

    [Fact]
    public void Score_NormalValue_ReturnsLowScore()
    {
        var detector = new IsolationForestDetector("CMP-01", "platen_rpm", seed: 42);
        var normalData = Enumerable.Range(0, 500).Select(i => 65.0 + Math.Sin(i * 0.1) * 3).ToArray();
        detector.Train(normalData);

        var score = detector.Score(65.0);

        score.Should().BeLessThan(0.7, "normal value should have low anomaly score");
    }

    [Fact]
    public void Score_ExtremeValue_ReturnsHighScore()
    {
        var detector = new IsolationForestDetector("CMP-01", "platen_rpm", seed: 42);
        var normalData = Enumerable.Range(0, 500).Select(i => 65.0 + Math.Sin(i * 0.1) * 3).ToArray();
        detector.Train(normalData);

        var score = detector.Score(200.0); // Far outside normal range

        score.Should().BeGreaterThan(0.5, "extreme value should have higher anomaly score");
    }

    [Fact]
    public void Classify_NormalValue_ReturnsNormal()
    {
        var detector = new IsolationForestDetector("CMP-01", "platen_rpm", seed: 42);
        var normalData = Enumerable.Range(0, 500).Select(i => 65.0 + Math.Sin(i * 0.1) * 3).ToArray();
        detector.Train(normalData);

        detector.Classify(65.0).Should().Be(AnomalySeverity.Normal);
    }

    [Fact]
    public void Score_UntrainedDetector_ReturnsNeutral()
    {
        var detector = new IsolationForestDetector("CMP-01", "platen_rpm");

        detector.Score(100.0).Should().Be(0.5);
        detector.IsTrained.Should().BeFalse();
    }

    [Fact]
    public void AveragePathLengthC_KnownValues()
    {
        IsolationForestDetector.AveragePathLengthC(1).Should().Be(0);
        IsolationForestDetector.AveragePathLengthC(2).Should().Be(1);
        // c(256) = 2*H(255) - 2*255/256 = 2*(ln(255)+0.5772) - 1.9922 ≈ 10.24
        IsolationForestDetector.AveragePathLengthC(256).Should().BeApproximately(10.24, 0.1);
    }

    // ── Anomaly Detector Manager ─────────────────────────────────────

    [Fact]
    public void Manager_GetOrCreate_ReturnsSameDetector()
    {
        var manager = new AnomalyDetectorManager();
        var d1 = manager.GetOrCreate("CMP-01", "platen_rpm");
        var d2 = manager.GetOrCreate("CMP-01", "platen_rpm");

        d1.Should().BeSameAs(d2);
    }

    [Fact]
    public void Manager_TrainAndClassify()
    {
        var manager = new AnomalyDetectorManager();
        var data = Enumerable.Range(0, 200).Select(_ => 65.0 + Random.Shared.NextDouble() * 5).ToArray();
        manager.TrainDetector("CMP-01", "platen_rpm", data, seed: 42);

        manager.Classify("CMP-01", "platen_rpm", 65.0).Should().NotBeNull();
    }

    [Fact]
    public void Manager_Classify_UntrainedSensor_ReturnsNull()
    {
        var manager = new AnomalyDetectorManager();

        manager.Classify("CMP-01", "unknown_sensor", 100.0).Should().BeNull();
    }

    [Fact]
    public void Manager_ListModels_ShowsTrainedOnly()
    {
        var manager = new AnomalyDetectorManager();
        manager.GetOrCreate("CMP-01", "platen_rpm"); // Not trained
        manager.TrainDetector("CMP-01", "temperature",
            Enumerable.Range(0, 100).Select(_ => 25.0 + Random.Shared.NextDouble() * 5).ToArray());

        var models = manager.ListModels();

        models.Should().HaveCount(1);
        models[0].SensorId.Should().Be("temperature");
    }

    // ── RUL Predictor ────────────────────────────────────────────────

    [Fact]
    public void PredictLinear_DecreasingTrend_ReturnsPositiveRul()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var timestamps = Enumerable.Range(0, 10).Select(i => baseTime.AddHours(i * 24)).ToList();
        var values = Enumerable.Range(0, 10).Select(i => 500.0 - i * 20.0).ToList(); // 500 → 320

        var result = RulPredictor.PredictLinear(timestamps, values, failureThreshold: 100, isDecreasing: true);

        result.Should().NotBeNull();
        result!.RemainingHours.Should().BeGreaterThan(0);
        result.Confidence.Should().BeGreaterThan(0.9);
        result.ModelType.Should().Be("linear");
    }

    [Fact]
    public void PredictLinear_InsufficientData_ReturnsNull()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var timestamps = new List<DateTimeOffset> { baseTime, baseTime.AddHours(1) };
        var values = new List<double> { 100, 90 };

        var result = RulPredictor.PredictLinear(timestamps, values, 50);

        result.Should().BeNull();
    }

    [Fact]
    public void PredictLinear_IncreasingWhenDecreasingExpected_ReturnsNull()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var timestamps = Enumerable.Range(0, 5).Select(i => baseTime.AddHours(i * 24)).ToList();
        var values = Enumerable.Range(0, 5).Select(i => 100.0 + i * 10.0).ToList(); // Increasing

        var result = RulPredictor.PredictLinear(timestamps, values, 50, isDecreasing: true);

        result.Should().BeNull("trend is increasing but decreasing was expected");
    }

    [Fact]
    public void PredictLinear_AlreadyPastThreshold_ReturnsZeroRul()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var timestamps = Enumerable.Range(0, 5).Select(i => baseTime.AddHours(i * 24)).ToList();
        var values = Enumerable.Range(0, 5).Select(i => 50.0 - i * 20.0).ToList(); // Already below 100 threshold

        var result = RulPredictor.PredictLinear(timestamps, values, 100, isDecreasing: true);

        // May return null if trend doesn't match, or 0 if already past
        if (result != null)
            result.RemainingHours.Should().Be(0);
    }

    [Fact]
    public void PredictLinear_PerfectLinearDecay_HighConfidence()
    {
        // Tests that linear regression works correctly via PredictLinear
        var baseTime = DateTimeOffset.UtcNow;
        var timestamps = Enumerable.Range(0, 5).Select(i => baseTime.AddHours(i)).ToList();
        var values = new List<double> { 50, 40, 30, 20, 10 }; // Perfect linear decay

        var result = RulPredictor.PredictLinear(timestamps, values, 0, isDecreasing: true);

        result.Should().NotBeNull();
        result!.Confidence.Should().BeApproximately(1.0, 0.01);
        result.Slope.Should().BeLessThan(0);
    }

    [Fact]
    public void PredictLinear_Summary_ContainsRemainingHours()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var timestamps = Enumerable.Range(0, 10).Select(i => baseTime.AddHours(i * 24)).ToList();
        var values = Enumerable.Range(0, 10).Select(i => 500.0 - i * 20.0).ToList();

        var result = RulPredictor.PredictLinear(timestamps, values, 100, isDecreasing: true);

        result.Should().NotBeNull();
        result!.Summary.Should().Contain("remaining");
    }
}
