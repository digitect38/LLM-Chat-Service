using System.Text.Json;
using FabCopilot.McpLogServer.Services;

namespace FabCopilot.McpLogServer.Interfaces;

public interface IMcpTool
{
    /// <summary>
    /// The unique name of this MCP tool (e.g., "search_logs", "extract_alarm_window").
    /// </summary>
    string ToolName { get; }

    /// <summary>
    /// Executes the tool with the given parameters and security context.
    /// Returns the result as a JsonElement for flexible serialization.
    /// </summary>
    Task<JsonElement> ExecuteAsync(JsonElement parameters, McpSecurityContext security, CancellationToken ct = default);
}
