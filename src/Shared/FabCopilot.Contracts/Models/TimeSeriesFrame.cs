using System.Text.Json;
using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Models;

public sealed class TimeSeriesFrame
{
    [JsonPropertyName("equipmentId")]
    public string EquipmentId { get; set; } = string.Empty;

    [JsonPropertyName("signals")]
    public List<string> Signals { get; set; } = [];

    [JsonPropertyName("start")]
    public DateTimeOffset Start { get; set; }

    [JsonPropertyName("end")]
    public DateTimeOffset End { get; set; }

    [JsonPropertyName("stepMs")]
    public int StepMs { get; set; }

    [JsonPropertyName("data")]
    public List<Dictionary<string, JsonElement>> Data { get; set; } = [];

    [JsonPropertyName("quality")]
    public TimeSeriesQuality? Quality { get; set; }
}

public sealed class TimeSeriesQuality
{
    [JsonPropertyName("missingRatio")]
    public double MissingRatio { get; set; }

    [JsonPropertyName("interpolated")]
    public bool Interpolated { get; set; }
}
