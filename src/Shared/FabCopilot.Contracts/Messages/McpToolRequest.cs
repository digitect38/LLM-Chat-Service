using System.Text.Json;
using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Messages;

public sealed class McpToolRequest
{
    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public JsonElement Parameters { get; set; }

    [JsonPropertyName("traceId")]
    public string TraceId { get; set; } = string.Empty;

    [JsonPropertyName("equipmentId")]
    public string EquipmentId { get; set; } = string.Empty;
}
