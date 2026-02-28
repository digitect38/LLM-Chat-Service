using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Models;

/// <summary>
/// Equipment registry entry — uniquely identifies a piece of equipment
/// and links it to its documentation layer and data tier.
/// </summary>
public sealed class EquipmentRegistration
{
    [JsonPropertyName("equipment_id")]
    public string EquipmentId { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("vendor")]
    public string Vendor { get; set; } = "";

    [JsonPropertyName("fab")]
    public string Fab { get; set; } = "";

    [JsonPropertyName("bay")]
    public string Bay { get; set; } = "";

    [JsonPropertyName("status")]
    public EquipmentStatus Status { get; set; } = EquipmentStatus.Idle;

    [JsonPropertyName("registered_at")]
    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("tags")]
    public Dictionary<string, string> Tags { get; set; } = new();
}

/// <summary>
/// Equipment operational status.
/// </summary>
public enum EquipmentStatus
{
    Idle,
    Running,
    Maintenance,
    Alarm,
    Offline
}
