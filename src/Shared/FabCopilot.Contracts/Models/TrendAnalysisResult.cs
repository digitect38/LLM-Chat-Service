using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Models;

/// <summary>
/// Result of trend analysis on time-series sensor data.
/// Includes moving averages, trend detection, and change points.
/// </summary>
public sealed class TrendAnalysisResult
{
    [JsonPropertyName("equipment_id")]
    public string EquipmentId { get; set; } = "";

    [JsonPropertyName("sensor_id")]
    public string SensorId { get; set; } = "";

    [JsonPropertyName("period")]
    public TimeSeriesPeriod Period { get; set; } = new();

    [JsonPropertyName("statistics")]
    public TimeSeriesStatistics Statistics { get; set; } = new();

    [JsonPropertyName("trend_direction")]
    public TrendDirection TrendDirection { get; set; }

    [JsonPropertyName("trend_slope")]
    public double TrendSlope { get; set; }

    [JsonPropertyName("change_points")]
    public List<ChangePoint> ChangePoints { get; set; } = [];

    [JsonPropertyName("moving_averages")]
    public List<(DateTimeOffset Timestamp, double Value)> MovingAverages { get; set; } = [];
}

public sealed class TimeSeriesPeriod
{
    [JsonPropertyName("start")]
    public DateTimeOffset Start { get; set; }

    [JsonPropertyName("end")]
    public DateTimeOffset End { get; set; }

    [JsonPropertyName("data_point_count")]
    public int DataPointCount { get; set; }
}

public sealed class TimeSeriesStatistics
{
    [JsonPropertyName("mean")]
    public double Mean { get; set; }

    [JsonPropertyName("std_dev")]
    public double StdDev { get; set; }

    [JsonPropertyName("min")]
    public double Min { get; set; }

    [JsonPropertyName("max")]
    public double Max { get; set; }

    [JsonPropertyName("median")]
    public double Median { get; set; }

    [JsonPropertyName("p5")]
    public double P5 { get; set; }

    [JsonPropertyName("p95")]
    public double P95 { get; set; }
}

public enum TrendDirection
{
    Stable,
    Increasing,
    Decreasing
}

/// <summary>
/// A detected change point in a time series.
/// </summary>
public sealed class ChangePoint
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("before_mean")]
    public double BeforeMean { get; set; }

    [JsonPropertyName("after_mean")]
    public double AfterMean { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
}
