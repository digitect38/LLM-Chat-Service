namespace FabCopilot.ServiceDashboard.Models;

/// <summary>
/// Time-bucketed error/warning counts per service.
/// </summary>
public sealed class ErrorRateBucket
{
    public DateTime BucketStart { get; set; }
    public string ServiceName { get; set; } = "";
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
}

/// <summary>
/// Grouped error template with occurrence counts.
/// </summary>
public sealed class ErrorTemplateGroup
{
    public string MessageTemplate { get; set; } = "";
    public string? ServiceName { get; set; }
    public int Count { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
}

/// <summary>
/// Per-service entry count summary.
/// </summary>
public sealed class ServiceHealthBucket
{
    public string ServiceName { get; set; } = "";
    public int TotalCount { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }
    public int DebugCount { get; set; }
}
