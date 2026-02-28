using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Models;

/// <summary>
/// A single sensor reading from equipment instrumentation.
/// Used for real-time streaming and batch ingestion.
/// </summary>
public sealed class SensorReading
{
    [JsonPropertyName("equipment_id")]
    public string EquipmentId { get; set; } = "";

    [JsonPropertyName("sensor_id")]
    public string SensorId { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("value")]
    public double Value { get; set; }

    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "";

    [JsonPropertyName("quality")]
    public DataQuality Quality { get; set; } = DataQuality.Good;
}

/// <summary>
/// Data quality indicator for sensor readings.
/// </summary>
public enum DataQuality
{
    Good,
    Uncertain,
    Bad,
    Interpolated
}

/// <summary>
/// Batch of sensor readings for efficient ingestion.
/// </summary>
public sealed class SensorBatch
{
    [JsonPropertyName("equipment_id")]
    public string EquipmentId { get; set; } = "";

    [JsonPropertyName("readings")]
    public List<SensorReading> Readings { get; set; } = [];

    [JsonPropertyName("batch_timestamp")]
    public DateTimeOffset BatchTimestamp { get; set; } = DateTimeOffset.UtcNow;
}
