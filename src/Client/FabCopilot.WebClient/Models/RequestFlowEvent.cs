namespace FabCopilot.WebClient.Models;

public sealed class RequestFlow
{
    public string CorrelationId { get; set; } = "";
    public List<FlowEvent> Events { get; set; } = [];
    public TimeSpan TotalDuration { get; set; }
}

public sealed class FlowEvent
{
    public DateTime Timestamp { get; set; }
    public string ServiceName { get; set; } = "";
    public string Message { get; set; } = "";
    public string Level { get; set; } = "INF";
    public TimeSpan GapFromPrevious { get; set; }
}
