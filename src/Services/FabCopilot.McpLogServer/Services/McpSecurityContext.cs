namespace FabCopilot.McpLogServer.Services;

/// <summary>
/// Security context for MCP tool execution. Enforces equipment scope, time range caps,
/// and record limits to prevent unbounded queries.
/// </summary>
public sealed class McpSecurityContext
{
    /// <summary>
    /// The equipment ID that this query is scoped to. Tools must only return data for this equipment.
    /// </summary>
    public string EquipmentId { get; set; } = string.Empty;

    /// <summary>
    /// The trace ID for correlation and audit logging.
    /// </summary>
    public string TraceId { get; set; } = string.Empty;

    /// <summary>
    /// Maximum allowed time range in minutes for log queries.
    /// </summary>
    public int MaxTimeRangeMinutes { get; set; } = 120;

    /// <summary>
    /// Maximum number of records that can be returned per query.
    /// </summary>
    public int MaxRecords { get; set; } = 5000;
}
