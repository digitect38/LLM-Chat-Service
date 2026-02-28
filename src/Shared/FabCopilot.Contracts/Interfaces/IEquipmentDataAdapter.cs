using FabCopilot.Contracts.Models;

namespace FabCopilot.Contracts.Interfaces;

/// <summary>
/// Protocol adapter interface for equipment data collection.
/// Implementations handle specific communication protocols
/// (SECS/GEM, OPC-UA, MQTT, REST, etc.) per Domain Pack.
/// </summary>
public interface IEquipmentDataAdapter
{
    /// <summary>Adapter identifier (e.g., "secs-gem", "opc-ua", "mock").</summary>
    string AdapterId { get; }

    /// <summary>Connects to the equipment data source.</summary>
    Task ConnectAsync(string connectionString, CancellationToken ct = default);

    /// <summary>Disconnects from the equipment data source.</summary>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>Returns true if currently connected and receiving data.</summary>
    bool IsConnected { get; }

    /// <summary>Reads the latest sensor values for the equipment.</summary>
    Task<List<SensorReading>> ReadSensorsAsync(string equipmentId, CancellationToken ct = default);

    /// <summary>Reads recent alarm/event history.</summary>
    Task<List<AlarmEvent>> ReadAlarmsAsync(string equipmentId, int maxCount = 100, CancellationToken ct = default);

    /// <summary>Reads PM (preventive maintenance) history.</summary>
    Task<List<PmRecord>> ReadPmHistoryAsync(string equipmentId, int maxCount = 50, CancellationToken ct = default);
}

/// <summary>
/// An alarm/event from equipment logs.
/// </summary>
public sealed class AlarmEvent
{
    public string EquipmentId { get; set; } = "";
    public string AlarmCode { get; set; } = "";
    public string Description { get; set; } = "";
    public string Severity { get; set; } = "Warning";
    public DateTimeOffset Timestamp { get; set; }
    public DateTimeOffset? ClearedAt { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
}

/// <summary>
/// Preventive maintenance record.
/// </summary>
public sealed class PmRecord
{
    public string EquipmentId { get; set; } = "";
    public string PmType { get; set; } = "";
    public string Technician { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<string> ItemsCompleted { get; set; } = [];
    public string Notes { get; set; } = "";
}
