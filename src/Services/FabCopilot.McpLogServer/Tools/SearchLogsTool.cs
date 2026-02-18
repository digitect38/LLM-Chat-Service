using System.Text.Json;
using FabCopilot.Contracts.Enums;
using FabCopilot.Contracts.Models;
using FabCopilot.McpLogServer.Interfaces;
using FabCopilot.McpLogServer.Services;

namespace FabCopilot.McpLogServer.Tools;

/// <summary>
/// MCP tool that searches equipment log records. Phase 1 stub returns mock data.
/// </summary>
public sealed class SearchLogsTool : IMcpTool
{
    public string ToolName => "search_logs";

    public Task<JsonElement> ExecuteAsync(JsonElement parameters, McpSecurityContext security, CancellationToken ct = default)
    {
        // Parse optional parameters
        var keyword = parameters.TryGetProperty("keyword", out var kw) ? kw.GetString() : null;
        var level = parameters.TryGetProperty("level", out var lv) ? lv.GetString() : null;
        var limit = parameters.TryGetProperty("limit", out var lim) ? lim.GetInt32() : 100;

        // Cap the limit
        limit = Math.Min(limit, security.MaxRecords);

        // Phase 1: Return mock log records
        var mockRecords = new List<LogRecord>
        {
            new()
            {
                Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
                EquipmentId = security.EquipmentId,
                Module = "PM1",
                Level = EquipmentLogLevel.Warning,
                Channel = "process",
                Event = "Pressure deviation detected",
                Raw = $"[WARN] PM1: Pressure deviation detected on {security.EquipmentId}"
            },
            new()
            {
                Timestamp = DateTimeOffset.UtcNow.AddMinutes(-3),
                EquipmentId = security.EquipmentId,
                Module = "PM1",
                Level = EquipmentLogLevel.Error,
                Channel = "alarm",
                Event = "Chamber pressure out of spec",
                Raw = $"[ERROR] PM1: Chamber pressure out of spec on {security.EquipmentId}"
            },
            new()
            {
                Timestamp = DateTimeOffset.UtcNow.AddMinutes(-1),
                EquipmentId = security.EquipmentId,
                Module = "PM1",
                Level = EquipmentLogLevel.Info,
                Channel = "system",
                Event = "Auto-recovery initiated",
                Raw = $"[INFO] PM1: Auto-recovery initiated on {security.EquipmentId}"
            }
        };

        // Filter by keyword if provided
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            mockRecords = mockRecords
                .Where(r => r.Raw?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true
                         || r.Event?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }

        var result = JsonSerializer.SerializeToElement(new
        {
            records = mockRecords.Take(limit),
            total = mockRecords.Count,
            equipmentId = security.EquipmentId
        });

        return Task.FromResult(result);
    }
}
