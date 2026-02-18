using System.Text.Json.Serialization;
using FabCopilot.Contracts.Enums;

namespace FabCopilot.Contracts.Models;

public sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public MessageRole Role { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("attachments")]
    public List<string>? Attachments { get; set; }

    [JsonPropertyName("toolResults")]
    public List<string>? ToolResultRefs { get; set; }
}
