using System.Text.Json;
using FabCopilot.Contracts.Interfaces;
using FabCopilot.Contracts.Models;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FabCopilot.Redis;

/// <summary>
/// Redis-backed equipment registry.
/// Stores equipment registrations as JSON in a Redis hash.
/// </summary>
public sealed class RedisEquipmentRegistry : IEquipmentRegistry
{
    private const string HashKey = "equipment:registry";
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisEquipmentRegistry> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public RedisEquipmentRegistry(IConnectionMultiplexer redis, ILogger<RedisEquipmentRegistry> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task RegisterAsync(EquipmentRegistration equipment, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(equipment, JsonOptions);
        await db.HashSetAsync(HashKey, equipment.EquipmentId, json);
        _logger.LogInformation("Equipment registered: {EquipmentId} ({Model}, {Type})",
            equipment.EquipmentId, equipment.Model, equipment.Type);
    }

    public async Task<EquipmentRegistration?> GetAsync(string equipmentId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var json = await db.HashGetAsync(HashKey, equipmentId);
        if (json.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<EquipmentRegistration>(json!, JsonOptions);
    }

    public async Task<List<EquipmentRegistration>> ListAsync(
        string? fab = null, string? type = null, EquipmentStatus? status = null,
        CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var entries = await db.HashGetAllAsync(HashKey);
        var results = new List<EquipmentRegistration>();

        foreach (var entry in entries)
        {
            if (entry.Value.IsNullOrEmpty) continue;
            var eq = JsonSerializer.Deserialize<EquipmentRegistration>(entry.Value!, JsonOptions);
            if (eq is null) continue;

            if (fab != null && !eq.Fab.Equals(fab, StringComparison.OrdinalIgnoreCase)) continue;
            if (type != null && !eq.Type.Equals(type, StringComparison.OrdinalIgnoreCase)) continue;
            if (status != null && eq.Status != status.Value) continue;

            results.Add(eq);
        }

        return results;
    }

    public async Task UpdateStatusAsync(string equipmentId, EquipmentStatus status, CancellationToken ct = default)
    {
        var eq = await GetAsync(equipmentId, ct);
        if (eq is null)
        {
            _logger.LogWarning("Equipment not found for status update: {EquipmentId}", equipmentId);
            return;
        }

        eq.Status = status;
        await RegisterAsync(eq, ct);
        _logger.LogInformation("Equipment status updated: {EquipmentId} → {Status}", equipmentId, status);
    }

    public async Task RemoveAsync(string equipmentId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        await db.HashDeleteAsync(HashKey, equipmentId);
        _logger.LogInformation("Equipment removed: {EquipmentId}", equipmentId);
    }
}
