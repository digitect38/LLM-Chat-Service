namespace FabCopilot.McpLogServer.Analysis;

/// <summary>
/// Remaining Useful Life (RUL) predictor using linear regression on sensor degradation trends.
/// Estimates time-to-threshold for consumable components.
/// </summary>
public static class RulPredictor
{
    /// <summary>
    /// Predicts remaining useful life based on a degradation trend.
    /// </summary>
    /// <param name="timestamps">Timestamps of observations.</param>
    /// <param name="values">Sensor values (e.g., pad thickness, conditioner wear).</param>
    /// <param name="failureThreshold">Value at which the component should be replaced.</param>
    /// <param name="isDecreasing">True if value decreases toward failure (e.g., pad thickness).</param>
    /// <returns>Predicted RUL in hours, or null if prediction is unreliable.</returns>
    public static RulPrediction? PredictLinear(
        IReadOnlyList<DateTimeOffset> timestamps,
        IReadOnlyList<double> values,
        double failureThreshold,
        bool isDecreasing = true)
    {
        if (timestamps.Count < 3 || timestamps.Count != values.Count)
            return null;

        // Convert timestamps to hours from first observation
        var baseTime = timestamps[0];
        var hours = timestamps.Select(t => (t - baseTime).TotalHours).ToArray();
        var vals = values.ToArray();

        // Linear regression: y = slope * x + intercept
        var (slope, intercept, rSquared) = LinearRegression(hours, vals);

        // Check if trend is in expected direction
        if (isDecreasing && slope >= 0) return null; // Not degrading
        if (!isDecreasing && slope <= 0) return null; // Not increasing

        // Predict time to threshold: threshold = slope * t + intercept
        // t = (threshold - intercept) / slope
        if (Math.Abs(slope) < 1e-12) return null;

        var timeToThreshold = (failureThreshold - intercept) / slope;
        var currentTime = hours[^1];
        var remainingHours = timeToThreshold - currentTime;

        if (remainingHours < 0) remainingHours = 0; // Already past threshold

        return new RulPrediction
        {
            RemainingHours = remainingHours,
            PredictedFailureTime = timestamps[^1].AddHours(remainingHours),
            Confidence = Math.Max(0, Math.Min(1, rSquared)), // R² as confidence
            Slope = slope,
            CurrentValue = vals[^1],
            FailureThreshold = failureThreshold,
            DataPoints = timestamps.Count
        };
    }

    /// <summary>
    /// Estimates RUL using exponential degradation model for faster wear patterns.
    /// y = a * e^(b*t)
    /// </summary>
    public static RulPrediction? PredictExponential(
        IReadOnlyList<DateTimeOffset> timestamps,
        IReadOnlyList<double> values,
        double failureThreshold,
        bool isDecreasing = true)
    {
        if (timestamps.Count < 3 || timestamps.Count != values.Count)
            return null;

        // Transform values to log space (only works for positive values)
        var positiveValues = values.Where(v => v > 0).ToList();
        if (positiveValues.Count < 3)
            return PredictLinear(timestamps, values, failureThreshold, isDecreasing);

        var baseTime = timestamps[0];
        var hours = new List<double>();
        var logVals = new List<double>();

        for (var i = 0; i < timestamps.Count; i++)
        {
            if (values[i] > 0)
            {
                hours.Add((timestamps[i] - baseTime).TotalHours);
                logVals.Add(Math.Log(values[i]));
            }
        }

        if (hours.Count < 3) return null;

        // Linear regression in log space
        var (slope, intercept, rSquared) = LinearRegression(hours.ToArray(), logVals.ToArray());

        // Predict time to threshold in log space: log(threshold) = slope * t + intercept
        if (Math.Abs(slope) < 1e-12 || failureThreshold <= 0) return null;

        var logThreshold = Math.Log(failureThreshold);
        var timeToThreshold = (logThreshold - intercept) / slope;
        var currentTime = hours[^1];
        var remainingHours = timeToThreshold - currentTime;

        if (remainingHours < 0) remainingHours = 0;

        // Compare R² with linear model to pick best fit
        var linearPrediction = PredictLinear(timestamps, values, failureThreshold, isDecreasing);
        if (linearPrediction != null && linearPrediction.Confidence > rSquared)
            return linearPrediction; // Linear fits better

        return new RulPrediction
        {
            RemainingHours = remainingHours,
            PredictedFailureTime = timestamps[^1].AddHours(remainingHours),
            Confidence = Math.Max(0, Math.Min(1, rSquared)),
            Slope = slope,
            CurrentValue = values[^1],
            FailureThreshold = failureThreshold,
            DataPoints = timestamps.Count,
            ModelType = "exponential"
        };
    }

    /// <summary>
    /// Computes linear regression: y = slope * x + intercept, with R² goodness of fit.
    /// </summary>
    internal static (double Slope, double Intercept, double RSquared) LinearRegression(double[] x, double[] y)
    {
        var n = x.Length;
        if (n < 2) return (0, y.Length > 0 ? y[0] : 0, 0);

        var sumX = x.Sum();
        var sumY = y.Sum();
        var sumXy = x.Zip(y, (xi, yi) => xi * yi).Sum();
        var sumX2 = x.Sum(xi => xi * xi);

        var meanX = sumX / n;
        var meanY = sumY / n;

        var denominator = sumX2 - sumX * sumX / n;
        if (Math.Abs(denominator) < 1e-12)
            return (0, meanY, 0);

        var slope = (sumXy - sumX * sumY / n) / denominator;
        var intercept = meanY - slope * meanX;

        // R²
        var ssRes = 0.0;
        var ssTot = 0.0;
        for (var i = 0; i < n; i++)
        {
            var predicted = slope * x[i] + intercept;
            ssRes += (y[i] - predicted) * (y[i] - predicted);
            ssTot += (y[i] - meanY) * (y[i] - meanY);
        }

        var rSquared = Math.Abs(ssTot) < 1e-12 ? 0 : 1 - ssRes / ssTot;

        return (slope, intercept, rSquared);
    }
}

/// <summary>
/// Remaining Useful Life prediction result.
/// </summary>
public sealed class RulPrediction
{
    /// <summary>Estimated remaining life in hours.</summary>
    public double RemainingHours { get; set; }

    /// <summary>Predicted failure time.</summary>
    public DateTimeOffset PredictedFailureTime { get; set; }

    /// <summary>Confidence score (R² of the regression model, 0~1).</summary>
    public double Confidence { get; set; }

    /// <summary>Degradation slope (units/hour).</summary>
    public double Slope { get; set; }

    /// <summary>Current sensor value.</summary>
    public double CurrentValue { get; set; }

    /// <summary>Value at which replacement is needed.</summary>
    public double FailureThreshold { get; set; }

    /// <summary>Number of data points used for prediction.</summary>
    public int DataPoints { get; set; }

    /// <summary>Model type used: "linear" or "exponential".</summary>
    public string ModelType { get; set; } = "linear";

    /// <summary>Human-readable summary.</summary>
    public string Summary => RemainingHours <= 0
        ? $"Component has reached end of life (current: {CurrentValue:F1}, threshold: {FailureThreshold:F1})"
        : $"Estimated {RemainingHours:F0} hours remaining (confidence: {Confidence:P0}). " +
          $"Predicted failure: {PredictedFailureTime:yyyy-MM-dd HH:mm}";
}
