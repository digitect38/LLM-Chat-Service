using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Models;

/// <summary>
/// Data retention policy for equipment telemetry.
/// Defines Hot → Warm → Cold tier transitions.
/// </summary>
public sealed class DataRetentionPolicy
{
    /// <summary>Hot tier: recent data on SSD, full resolution. Default: 7 days.</summary>
    [JsonPropertyName("hot_days")]
    public int HotDays { get; set; } = 7;

    /// <summary>Warm tier: medium-term data on HDD, downsampled. Default: 90 days.</summary>
    [JsonPropertyName("warm_days")]
    public int WarmDays { get; set; } = 90;

    /// <summary>Cold tier: archived data, compressed. Default: 365 days (0 = forever).</summary>
    [JsonPropertyName("cold_days")]
    public int ColdDays { get; set; } = 365;

    /// <summary>Downsampling interval for warm tier (seconds). Default: 60.</summary>
    [JsonPropertyName("warm_downsample_sec")]
    public int WarmDownsampleSec { get; set; } = 60;

    /// <summary>Downsampling interval for cold tier (seconds). Default: 300.</summary>
    [JsonPropertyName("cold_downsample_sec")]
    public int ColdDownsampleSec { get; set; } = 300;
}
