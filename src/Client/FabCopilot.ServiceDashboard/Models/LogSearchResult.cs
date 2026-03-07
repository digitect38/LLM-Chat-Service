namespace FabCopilot.ServiceDashboard.Models;

public sealed class LogSearchResult
{
    public List<JsonLogEntry> Entries { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public TimeSpan SearchDuration { get; set; }
}
