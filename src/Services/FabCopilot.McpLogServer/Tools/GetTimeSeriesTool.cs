using System.Text.Json;
using FabCopilot.Contracts.Models;
using FabCopilot.McpLogServer.Interfaces;
using FabCopilot.McpLogServer.Services;

namespace FabCopilot.McpLogServer.Tools;

/// <summary>
/// MCP tool that retrieves time series data for equipment signals.
/// Phase 1 stub returns mock timeseries data.
/// </summary>
public sealed class GetTimeSeriesTool : IMcpTool
{
    public string ToolName => "get_timeseries";

    public Task<JsonElement> ExecuteAsync(JsonElement parameters, McpSecurityContext security, CancellationToken ct = default)
    {
        var signals = new List<string>();
        if (parameters.TryGetProperty("signals", out var sigArray) && sigArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var sig in sigArray.EnumerateArray())
            {
                var val = sig.GetString();
                if (val is not null) signals.Add(val);
            }
        }

        if (signals.Count == 0)
        {
            signals.AddRange(["temperature", "pressure", "flow_rate"]);
        }

        var startStr = parameters.TryGetProperty("start", out var s) ? s.GetString() : null;
        var endStr = parameters.TryGetProperty("end", out var e) ? e.GetString() : null;
        var stepMs = parameters.TryGetProperty("stepMs", out var st) ? st.GetInt32() : 1000;

        var start = startStr is not null ? DateTimeOffset.Parse(startStr) : DateTimeOffset.UtcNow.AddMinutes(-10);
        var end = endStr is not null ? DateTimeOffset.Parse(endStr) : DateTimeOffset.UtcNow;

        // Cap time range to the security policy
        var requestedMinutes = (end - start).TotalMinutes;
        if (requestedMinutes > security.MaxTimeRangeMinutes)
        {
            start = end.AddMinutes(-security.MaxTimeRangeMinutes);
        }

        // Phase 1: Return mock timeseries data
        var random = new Random(42); // Deterministic seed for consistent mock data
        var data = new List<Dictionary<string, JsonElement>>();
        var current = start;

        while (current <= end && data.Count < security.MaxRecords)
        {
            var point = new Dictionary<string, JsonElement>
            {
                ["ts"] = JsonSerializer.SerializeToElement(current.ToString("O"))
            };

            foreach (var signal in signals)
            {
                var value = signal switch
                {
                    "temperature" => 350.0 + random.NextDouble() * 10,
                    "pressure" => 1.5 + random.NextDouble() * 0.5,
                    "flow_rate" => 100.0 + random.NextDouble() * 20,
                    _ => random.NextDouble() * 100
                };
                point[signal] = JsonSerializer.SerializeToElement(Math.Round(value, 3));
            }

            data.Add(point);
            current = current.AddMilliseconds(stepMs);
        }

        var frame = new TimeSeriesFrame
        {
            EquipmentId = security.EquipmentId,
            Signals = signals,
            Start = start,
            End = end,
            StepMs = stepMs,
            Data = data,
            Quality = new TimeSeriesQuality
            {
                MissingRatio = 0.0,
                Interpolated = false
            }
        };

        var result = JsonSerializer.SerializeToElement(frame);
        return Task.FromResult(result);
    }
}
