using FabCopilot.Contracts.Messages;

namespace FabCopilot.Integration.Tests.Infrastructure;

public sealed class ChatResponse
{
    public string ConversationId { get; set; } = string.Empty;
    public string FullText { get; set; } = string.Empty;
    public List<CitationInfo> Citations { get; set; } = [];
    public string? Error { get; set; }
    public TimeSpan TimeToFirstToken { get; set; }
    public TimeSpan TotalTime { get; set; }
    public int TokenCount { get; set; }
}
