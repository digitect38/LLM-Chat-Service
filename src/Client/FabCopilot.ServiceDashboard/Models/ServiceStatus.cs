namespace FabCopilot.ServiceDashboard.Models;

public sealed class ServiceStatus
{
    public required string ServiceName { get; init; }
    public ServiceState State { get; set; } = ServiceState.Unknown;
    public long ResponseTimeMs { get; set; }
    public DateTimeOffset LastChecked { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum ServiceState
{
    Unknown,
    Up,
    Down,
    Degraded
}
