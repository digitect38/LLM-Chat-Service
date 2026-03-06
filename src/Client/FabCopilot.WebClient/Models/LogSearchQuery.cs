namespace FabCopilot.WebClient.Models;

public sealed class LogSearchQuery
{
    public string? Keyword { get; set; }
    public string? ServiceName { get; set; }
    public HashSet<string> Levels { get; set; } = ["INF", "WRN", "ERR", "DBG"];
    public string? CorrelationId { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}
