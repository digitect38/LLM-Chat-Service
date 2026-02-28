using FabCopilot.Contracts.Models;

namespace FabCopilot.McpLogServer.Analysis;

/// <summary>
/// Time-series analysis engine for equipment sensor data.
/// Implements statistical analysis, trend detection, and anomaly detection
/// using lightweight in-process algorithms (no external ML framework required).
/// </summary>
public static class TimeSeriesAnalyzer
{
    /// <summary>
    /// Computes basic statistics for a time series.
    /// </summary>
    public static TimeSeriesStatistics ComputeStatistics(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return new TimeSeriesStatistics();

        var sorted = values.OrderBy(v => v).ToList();
        var mean = values.Average();
        var variance = values.Average(v => (v - mean) * (v - mean));

        return new TimeSeriesStatistics
        {
            Mean = mean,
            StdDev = Math.Sqrt(variance),
            Min = sorted[0],
            Max = sorted[^1],
            Median = Percentile(sorted, 0.50),
            P5 = Percentile(sorted, 0.05),
            P95 = Percentile(sorted, 0.95)
        };
    }

    /// <summary>
    /// Computes a simple moving average (SMA) over the given window size.
    /// </summary>
    public static List<double> MovingAverage(IReadOnlyList<double> values, int windowSize)
    {
        if (values.Count == 0 || windowSize <= 0) return [];

        var result = new List<double>();
        var window = Math.Min(windowSize, values.Count);

        for (var i = 0; i <= values.Count - window; i++)
        {
            var sum = 0.0;
            for (var j = 0; j < window; j++)
                sum += values[i + j];
            result.Add(sum / window);
        }

        return result;
    }

    /// <summary>
    /// Computes exponential moving average (EMA).
    /// Alpha = 2 / (span + 1).
    /// </summary>
    public static List<double> ExponentialMovingAverage(IReadOnlyList<double> values, int span)
    {
        if (values.Count == 0 || span <= 0) return [];

        var alpha = 2.0 / (span + 1);
        var result = new List<double> { values[0] };

        for (var i = 1; i < values.Count; i++)
        {
            result.Add(alpha * values[i] + (1 - alpha) * result[^1]);
        }

        return result;
    }

    /// <summary>
    /// Detects the overall trend direction using linear regression.
    /// Returns (slope, direction).
    /// </summary>
    public static (double Slope, TrendDirection Direction) DetectTrend(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
            return (0, TrendDirection.Stable);

        // Simple linear regression: y = slope * x + intercept
        var n = values.Count;
        var sumX = 0.0;
        var sumY = 0.0;
        var sumXY = 0.0;
        var sumX2 = 0.0;

        for (var i = 0; i < n; i++)
        {
            sumX += i;
            sumY += values[i];
            sumXY += i * values[i];
            sumX2 += (double)i * i;
        }

        var slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);

        // Normalize slope by mean to get relative change rate
        var mean = sumY / n;
        var relativeSlope = mean != 0 ? slope / Math.Abs(mean) : slope;

        var direction = Math.Abs(relativeSlope) < 0.001 ? TrendDirection.Stable
            : relativeSlope > 0 ? TrendDirection.Increasing
            : TrendDirection.Decreasing;

        return (slope, direction);
    }

    /// <summary>
    /// Detects change points using CUSUM (Cumulative Sum) algorithm.
    /// Identifies points where the mean shifts significantly.
    /// </summary>
    public static List<int> DetectChangePointsCusum(
        IReadOnlyList<double> values,
        double threshold = 5.0,
        double drift = 0.5)
    {
        if (values.Count < 10) return [];

        var mean = values.Average();
        var stdDev = Math.Sqrt(values.Average(v => (v - mean) * (v - mean)));
        if (stdDev < 1e-10) return [];

        var changePoints = new List<int>();
        var cusumPos = 0.0;
        var cusumNeg = 0.0;
        var scaledThreshold = threshold * stdDev;
        var scaledDrift = drift * stdDev;

        for (var i = 0; i < values.Count; i++)
        {
            cusumPos = Math.Max(0, cusumPos + (values[i] - mean) - scaledDrift);
            cusumNeg = Math.Max(0, cusumNeg - (values[i] - mean) - scaledDrift);

            if (cusumPos > scaledThreshold || cusumNeg > scaledThreshold)
            {
                changePoints.Add(i);
                cusumPos = 0;
                cusumNeg = 0;
            }
        }

        return changePoints;
    }

    /// <summary>
    /// Statistical anomaly detection using 3-sigma rule (Z-score).
    /// Returns indices and severity of anomalous values.
    /// </summary>
    public static List<(int Index, AnomalySeverity Severity, double ZScore)> DetectAnomalies3Sigma(
        IReadOnlyList<double> values,
        double cautionSigma = 2.0,
        double anomalySigma = 3.0)
    {
        if (values.Count < 5) return [];

        var stats = ComputeStatistics(values);
        if (stats.StdDev < 1e-10) return [];

        var results = new List<(int, AnomalySeverity, double)>();

        for (var i = 0; i < values.Count; i++)
        {
            var z = Math.Abs(values[i] - stats.Mean) / stats.StdDev;

            if (z >= anomalySigma)
                results.Add((i, AnomalySeverity.Anomaly, z));
            else if (z >= cautionSigma)
                results.Add((i, AnomalySeverity.Caution, z));
        }

        return results;
    }

    /// <summary>
    /// Simple Isolation Forest-inspired anomaly detection.
    /// Uses local density estimation: points far from the mean of
    /// their local neighborhood are considered anomalous.
    /// </summary>
    public static List<(int Index, double AnomalyScore)> DetectAnomaliesLocal(
        IReadOnlyList<double> values, int windowSize = 20, double threshold = 2.5)
    {
        if (values.Count < windowSize * 2) return [];

        var results = new List<(int, double)>();
        var halfWindow = windowSize / 2;

        for (var i = halfWindow; i < values.Count - halfWindow; i++)
        {
            var localValues = new List<double>();
            for (var j = i - halfWindow; j <= i + halfWindow; j++)
            {
                if (j != i) localValues.Add(values[j]);
            }

            var localMean = localValues.Average();
            var localStd = Math.Sqrt(localValues.Average(v => (v - localMean) * (v - localMean)));

            if (localStd < 1e-10) continue;

            var score = Math.Abs(values[i] - localMean) / localStd;
            if (score > threshold)
            {
                results.Add((i, score));
            }
        }

        return results;
    }

    /// <summary>
    /// Computes cross-correlation between two sensor signals.
    /// Returns the Pearson correlation coefficient.
    /// </summary>
    public static double CrossCorrelation(IReadOnlyList<double> signalA, IReadOnlyList<double> signalB)
    {
        var n = Math.Min(signalA.Count, signalB.Count);
        if (n < 2) return 0;

        var meanA = 0.0;
        var meanB = 0.0;
        for (var i = 0; i < n; i++)
        {
            meanA += signalA[i];
            meanB += signalB[i];
        }
        meanA /= n;
        meanB /= n;

        var cov = 0.0;
        var varA = 0.0;
        var varB = 0.0;

        for (var i = 0; i < n; i++)
        {
            var da = signalA[i] - meanA;
            var db = signalB[i] - meanB;
            cov += da * db;
            varA += da * da;
            varB += db * db;
        }

        var denominator = Math.Sqrt(varA * varB);
        return denominator < 1e-10 ? 0 : cov / denominator;
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        var index = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Max(0, Math.Min(index, sorted.Count - 1))];
    }
}
