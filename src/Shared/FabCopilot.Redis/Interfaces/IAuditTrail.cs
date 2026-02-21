namespace FabCopilot.Redis.Interfaces;

/// <summary>
/// Logs query, response, and feedback events to a persistent audit trail.
/// </summary>
public interface IAuditTrail
{
    /// <summary>
    /// Logs a user query event.
    /// </summary>
    Task LogQueryAsync(string equipmentId, string conversationId, string query, CancellationToken ct = default);

    /// <summary>
    /// Logs an LLM response event.
    /// </summary>
    Task LogResponseAsync(string equipmentId, string conversationId, int responseLength, double durationMs, CancellationToken ct = default);

    /// <summary>
    /// Logs a user feedback event.
    /// </summary>
    Task LogFeedbackAsync(string equipmentId, string conversationId, bool isPositive, CancellationToken ct = default);

    /// <summary>
    /// Retrieves recent audit events for a given equipment, ordered by most recent first.
    /// </summary>
    Task<IReadOnlyList<AuditEvent>> GetRecentEventsAsync(string equipmentId, int limit = 50, CancellationToken ct = default);

    /// <summary>
    /// Retrieves recent audit events across all equipment, ordered by most recent first.
    /// </summary>
    Task<IReadOnlyList<AuditEvent>> GetRecentEventsAsync(int limit = 100, CancellationToken ct = default);
}

public sealed class AuditEvent
{
    public string EventType { get; set; } = string.Empty;
    public string EquipmentId { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}
