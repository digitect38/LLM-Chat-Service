using FabCopilot.Contracts.Interfaces;
using FabCopilot.Contracts.Models;

namespace FabCopilot.McpLogServer.Adapters;

/// <summary>
/// Mock equipment data adapter for development and testing.
/// Generates simulated sensor data, alarms, and PM history.
/// </summary>
public sealed class MockEquipmentDataAdapter : IEquipmentDataAdapter
{
    private static readonly Random Rng = new();

    public string AdapterId => "mock";
    public bool IsConnected => true;

    public Task ConnectAsync(string connectionString, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<List<SensorReading>> ReadSensorsAsync(string equipmentId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var readings = new List<SensorReading>
        {
            new()
            {
                EquipmentId = equipmentId,
                SensorId = "platen_rpm",
                Timestamp = now,
                Value = 60 + Rng.NextDouble() * 10,
                Unit = "rpm"
            },
            new()
            {
                EquipmentId = equipmentId,
                SensorId = "head_pressure",
                Timestamp = now,
                Value = 3.0 + Rng.NextDouble() * 1.0,
                Unit = "psi"
            },
            new()
            {
                EquipmentId = equipmentId,
                SensorId = "slurry_flow_rate",
                Timestamp = now,
                Value = 150 + Rng.NextDouble() * 30,
                Unit = "ml/min"
            },
            new()
            {
                EquipmentId = equipmentId,
                SensorId = "platen_temperature",
                Timestamp = now,
                Value = 25 + Rng.NextDouble() * 5,
                Unit = "°C"
            },
            new()
            {
                EquipmentId = equipmentId,
                SensorId = "pad_life_hours",
                Timestamp = now,
                Value = 200 + Rng.NextDouble() * 300,
                Unit = "hours"
            },
            new()
            {
                EquipmentId = equipmentId,
                SensorId = "conditioner_current",
                Timestamp = now,
                Value = 1.5 + Rng.NextDouble() * 0.5,
                Unit = "A"
            }
        };

        return Task.FromResult(readings);
    }

    public Task<List<AlarmEvent>> ReadAlarmsAsync(string equipmentId, int maxCount = 100, CancellationToken ct = default)
    {
        var alarms = new List<AlarmEvent>
        {
            new()
            {
                EquipmentId = equipmentId,
                AlarmCode = "A100",
                Description = "Emergency Stop activated",
                Severity = "Critical",
                Timestamp = DateTimeOffset.UtcNow.AddHours(-2),
                ClearedAt = DateTimeOffset.UtcNow.AddHours(-1.5)
            },
            new()
            {
                EquipmentId = equipmentId,
                AlarmCode = "A201",
                Description = "Slurry flow rate below threshold",
                Severity = "Warning",
                Timestamp = DateTimeOffset.UtcNow.AddHours(-5)
            },
            new()
            {
                EquipmentId = equipmentId,
                AlarmCode = "A305",
                Description = "Temperature sensor deviation",
                Severity = "Warning",
                Timestamp = DateTimeOffset.UtcNow.AddDays(-1),
                ClearedAt = DateTimeOffset.UtcNow.AddDays(-1).AddMinutes(30)
            }
        };

        return Task.FromResult(alarms.Take(maxCount).ToList());
    }

    public Task<List<PmRecord>> ReadPmHistoryAsync(string equipmentId, int maxCount = 50, CancellationToken ct = default)
    {
        var records = new List<PmRecord>
        {
            new()
            {
                EquipmentId = equipmentId,
                PmType = "Daily",
                Technician = "Kim",
                StartedAt = DateTimeOffset.UtcNow.AddDays(-1),
                CompletedAt = DateTimeOffset.UtcNow.AddDays(-1).AddMinutes(30),
                ItemsCompleted = ["슬러리 레벨 확인", "패드 표면 검사", "DI water 유량 체크"]
            },
            new()
            {
                EquipmentId = equipmentId,
                PmType = "Weekly",
                Technician = "Park",
                StartedAt = DateTimeOffset.UtcNow.AddDays(-7),
                CompletedAt = DateTimeOffset.UtcNow.AddDays(-7).AddHours(2),
                ItemsCompleted = ["컨디셔너 디스크 검사", "배기 필터 교체", "압력 센서 교정"]
            },
            new()
            {
                EquipmentId = equipmentId,
                PmType = "Monthly",
                Technician = "Lee",
                StartedAt = DateTimeOffset.UtcNow.AddDays(-30),
                CompletedAt = DateTimeOffset.UtcNow.AddDays(-30).AddHours(4),
                ItemsCompleted = ["패드 교체", "슬러리 라인 플러시", "Qualification 검증"],
                Notes = "패드 사용 시간 480시간, MRR 5% 저하 확인"
            }
        };

        return Task.FromResult(records.Take(maxCount).ToList());
    }
}
