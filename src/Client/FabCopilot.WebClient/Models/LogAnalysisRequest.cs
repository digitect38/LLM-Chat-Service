namespace FabCopilot.WebClient.Models;

public sealed class LogAnalysisRequest
{
    public string UserMessage { get; set; } = string.Empty;
    public string LogContext { get; set; } = string.Empty;
}
