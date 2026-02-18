using System.Text.Json;
using FabCopilot.McpLogServer.Interfaces;
using FabCopilot.McpLogServer.Services;

namespace FabCopilot.McpLogServer.Tools;

/// <summary>
/// MCP tool that detects anomalies in equipment log and time series data.
/// Phase 1 stub -- returns placeholder anomaly detection results.
/// </summary>
public sealed class DetectAnomaliesTool : IMcpTool
{
    public string ToolName => "detect_anomalies";

    public Task<JsonElement> ExecuteAsync(JsonElement parameters, McpSecurityContext security, CancellationToken ct = default)
    {
        // Phase 1 stub: return mock anomaly detection results
        var result = JsonSerializer.SerializeToElement(new
        {
            equipmentId = security.EquipmentId,
            anomalies = new[]
            {
                new
                {
                    type = "spike",
                    signal = "pressure",
                    timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
                    severity = "warning",
                    description = "Anomaly detection is not yet implemented. This is a Phase 1 stub.",
                    confidence = 0.0
                }
            },
            analysisWindow = new
            {
                start = DateTimeOffset.UtcNow.AddHours(-1),
                end = DateTimeOffset.UtcNow
            },
            status = "stub"
        });

        return Task.FromResult(result);
    }
}
