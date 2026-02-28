using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Models;

/// <summary>
/// Result of anomaly detection on time-series sensor data.
/// </summary>
public sealed class AnomalyResult
{
    [JsonPropertyName("equipment_id")]
    public string EquipmentId { get; set; } = "";

    [JsonPropertyName("sensor_id")]
    public string SensorId { get; set; } = "";

    [JsonPropertyName("anomaly_type")]
    public AnomalyType Type { get; set; }

    [JsonPropertyName("severity")]
    public AnomalySeverity Severity { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("detected_at")]
    public DateTimeOffset DetectedAt { get; set; }

    [JsonPropertyName("value")]
    public double Value { get; set; }

    [JsonPropertyName("expected_range")]
    public (double Lower, double Upper) ExpectedRange { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
}

public enum AnomalyType
{
    SpikeUp,
    SpikeDown,
    Drift,
    LevelShift,
    VarianceChange,
    Trend
}

public enum AnomalySeverity
{
    Normal,
    Caution,
    Anomaly
}
