using System.Text.Json;
using FabCopilot.Redis.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FabCopilot.Redis;

/// <summary>
/// Redis sorted-set-backed audit trail. Events are stored in two sorted sets:
///   - fab:audit:{equipmentId} — per-equipment audit log
///   - fab:audit:global         — all events across equipment
/// Score = Unix timestamp (milliseconds) for chronological ordering.
/// </summary>
public sealed class RedisAuditTrail : IAuditTrail
{
    private const string KeyPrefix = "fab:audit:";
    private const string GlobalKey = "fab:audit:global";
    private const int MaxEventsPerSet = 10_000;

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisAuditTrail> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RedisAuditTrail(IConnectionMultiplexer redis, ILogger<RedisAuditTrail> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task LogQueryAsync(string equipmentId, string conversationId, string query, CancellationToken ct = default)
    {
        var evt = new AuditEvent
        {
            EventType = "query",
            EquipmentId = equipmentId,
            ConversationId = conversationId,
            Data = Truncate(query, 500),
            Timestamp = DateTimeOffset.UtcNow
        };

        await WriteEventAsync(evt);
    }

    public async Task LogResponseAsync(string equipmentId, string conversationId, int responseLength, double durationMs, CancellationToken ct = default)
    {
        var evt = new AuditEvent
        {
            EventType = "response",
            EquipmentId = equipmentId,
            ConversationId = conversationId,
            Data = $"length={responseLength}, duration={durationMs:F0}ms",
            Timestamp = DateTimeOffset.UtcNow
        };

        await WriteEventAsync(evt);
    }

    public async Task LogFeedbackAsync(string equipmentId, string conversationId, bool isPositive, CancellationToken ct = default)
    {
        var evt = new AuditEvent
        {
            EventType = "feedback",
            EquipmentId = equipmentId,
            ConversationId = conversationId,
            Data = isPositive ? "positive" : "negative",
            Timestamp = DateTimeOffset.UtcNow
        };

        await WriteEventAsync(evt);
    }

    public async Task<IReadOnlyList<AuditEvent>> GetRecentEventsAsync(string equipmentId, int limit = 50, CancellationToken ct = default)
    {
        return await ReadEventsAsync($"{KeyPrefix}{equipmentId}", limit);
    }

    public async Task<IReadOnlyList<AuditEvent>> GetRecentEventsAsync(int limit = 100, CancellationToken ct = default)
    {
        return await ReadEventsAsync(GlobalKey, limit);
    }

    private async Task WriteEventAsync(AuditEvent evt)
    {
        try
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(evt, JsonOptions);
            var score = evt.Timestamp.ToUnixTimeMilliseconds();

            var equipmentKey = $"{KeyPrefix}{evt.EquipmentId}";

            // Write to both per-equipment and global sorted sets
            await Task.WhenAll(
                db.SortedSetAddAsync(equipmentKey, json, score),
                db.SortedSetAddAsync(GlobalKey, json, score));

            // Trim old entries to prevent unbounded growth (fire-and-forget)
            _ = db.SortedSetRemoveRangeByRankAsync(equipmentKey, 0, -MaxEventsPerSet - 1);
            _ = db.SortedSetRemoveRangeByRankAsync(GlobalKey, 0, -MaxEventsPerSet - 1);
        }
        catch (Exception ex)
        {
            // Audit trail failures must not crash the service
            _logger.LogWarning(ex, "Failed to write audit event: {EventType} for {EquipmentId}",
                evt.EventType, evt.EquipmentId);
        }
    }

    private async Task<IReadOnlyList<AuditEvent>> ReadEventsAsync(string key, int limit)
    {
        try
        {
            var db = _redis.GetDatabase();
            // Get most recent events (highest scores = most recent timestamps)
            var entries = await db.SortedSetRangeByRankAsync(key, start: -limit, stop: -1, order: Order.Descending);

            var events = new List<AuditEvent>(entries.Length);
            foreach (var entry in entries)
            {
                var str = entry.ToString();
                if (string.IsNullOrEmpty(str)) continue;

                try
                {
                    var evt = JsonSerializer.Deserialize<AuditEvent>(str, JsonOptions);
                    if (evt is not null)
                        events.Add(evt);
                }
                catch (JsonException)
                {
                    // Skip malformed entries
                }
            }

            return events;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read audit events from {Key}", key);
            return [];
        }
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
