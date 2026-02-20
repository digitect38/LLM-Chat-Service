using System.Text.Json;
using System.Text.Json.Serialization;

namespace FabCopilot.Contracts.Messages;

public sealed class McpToolResult
{
    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public JsonElement Result { get; set; }

    [JsonPropertyName("traceId")]
    public string TraceId { get; set; } = string.Empty;

    [JsonPropertyName("stats")]
    public McpToolStats? Stats { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class McpToolStats
{
    [JsonPropertyName("matched")]
    public int Matched { get; set; }

    [JsonPropertyName("returned")]
    public int Returned { get; set; }

    [JsonPropertyName("tookMs")]
    public long TookMs { get; set; }
}
