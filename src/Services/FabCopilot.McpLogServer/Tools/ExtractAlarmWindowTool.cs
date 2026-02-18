using System.Text.Json;
using FabCopilot.Contracts.Models;
using FabCopilot.McpLogServer.Interfaces;
using FabCopilot.McpLogServer.Services;

namespace FabCopilot.McpLogServer.Tools;

/// <summary>
/// MCP tool that extracts an alarm window -- log records surrounding an alarm trigger time.
/// Phase 1 stub returns mock alarm window data.
/// </summary>
public sealed class ExtractAlarmWindowTool : IMcpTool
{
    public string ToolName => "extract_alarm_window";

    public Task<JsonElement> ExecuteAsync(JsonElement parameters, McpSecurityContext security, CancellationToken ct = default)
    {
        var alarmCode = parameters.TryGetProperty("alarmCode", out var ac) ? ac.GetString() ?? "UNKNOWN" : "UNKNOWN";
        var triggeredAt = parameters.TryGetProperty("triggeredAt", out var ta)
            ? DateTimeOffset.Parse(ta.GetString()!)
            : DateTimeOffset.UtcNow;
        var windowBeforeSec = parameters.TryGetProperty("windowBeforeSec", out var wb) ? wb.GetInt32() : 300;
        var windowAfterSec = parameters.TryGetProperty("windowAfterSec", out var wa) ? wa.GetInt32() : 300;

        // Cap time range to the security policy
        var totalWindowMinutes = (windowBeforeSec + windowAfterSec) / 60.0;
        if (totalWindowMinutes > security.MaxTimeRangeMinutes)
        {
            var scale = security.MaxTimeRangeMinutes / totalWindowMinutes;
            windowBeforeSec = (int)(windowBeforeSec * scale);
            windowAfterSec = (int)(windowAfterSec * scale);
        }

        // Phase 1: Return mock alarm window data
        var alarmWindow = new AlarmWindow
        {
            AlarmCode = alarmCode,
            EquipmentId = security.EquipmentId,
            TriggeredAt = triggeredAt,
            WindowBeforeSec = windowBeforeSec,
            WindowAfterSec = windowAfterSec,
            RecordsRef = $"alarm-window:{security.EquipmentId}:{alarmCode}:{triggeredAt:yyyyMMddHHmmss}"
        };

        var result = JsonSerializer.SerializeToElement(new
        {
            alarmWindow,
            logSummary = new
            {
                totalRecords = 42,
                warningCount = 8,
                errorCount = 3,
                timeRange = new
                {
                    start = triggeredAt.AddSeconds(-windowBeforeSec),
                    end = triggeredAt.AddSeconds(windowAfterSec)
                }
            }
        });

        return Task.FromResult(result);
    }
}
