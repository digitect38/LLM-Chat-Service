using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FabCopilot.Contracts.Messages;
using FabCopilot.RagService.Configuration;
using FabCopilot.RagService.Interfaces;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace FabCopilot.RagService.Services;

public sealed class RedisRagCache : IRagCache
{
    private const string CachePrefix = "fab:ragcache:";
    private const string CacheKeySet = "fab:ragcache:keys";
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    private readonly IDatabase _db;
    private readonly RagOptions _ragOptions;
    private readonly ILogger<RedisRagCache> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public RedisRagCache(
        IConnectionMultiplexer redis,
        IOptions<RagOptions> ragOptions,
        ILogger<RedisRagCache> logger)
    {
        _db = redis.GetDatabase();
        _ragOptions = ragOptions.Value;
        _logger = logger;
    }

    public async Task<RagResponse?> GetAsync(string query, string equipmentId, string pipelineMode, int topK, CancellationToken ct = default)
    {
        if (!_ragOptions.EnableRagCache) return null;

        var key = BuildKey(query, equipmentId, pipelineMode, topK);

        try
        {
            var json = await _db.StringGetAsync(key).ConfigureAwait(false);
            if (json.IsNullOrEmpty)
                return null;

            _logger.LogDebug("RAG cache hit. Key={Key}", key);
            return JsonSerializer.Deserialize<RagResponse>(json!, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RAG cache read failed. Key={Key}", key);
            return null;
        }
    }

    public async Task SetAsync(string query, string equipmentId, string pipelineMode, int topK, RagResponse response, CancellationToken ct = default)
    {
        if (!_ragOptions.EnableRagCache) return;

        var key = BuildKey(query, equipmentId, pipelineMode, topK);

        try
        {
            var json = JsonSerializer.Serialize(response, JsonOptions);
            var ttl = TimeSpan.FromHours(_ragOptions.RagCacheTtlHours);

            await _db.StringSetAsync(key, json, ttl).ConfigureAwait(false);
            await _db.SetAddAsync(CacheKeySet, key).ConfigureAwait(false);

            _logger.LogDebug("RAG cache set. Key={Key}, TTL={TTL}h", key, ttl.TotalHours);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RAG cache write failed. Key={Key}", key);
        }
    }

    public async Task InvalidateAllAsync(CancellationToken ct = default)
    {
        try
        {
            var keys = await _db.SetMembersAsync(CacheKeySet).ConfigureAwait(false);
            if (keys.Length > 0)
            {
                var redisKeys = keys.Select(k => (RedisKey)(string)k!).ToArray();
                await _db.KeyDeleteAsync(redisKeys).ConfigureAwait(false);
                await _db.KeyDeleteAsync(CacheKeySet).ConfigureAwait(false);

                _logger.LogInformation("RAG cache invalidated. Deleted {Count} entries", keys.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RAG cache invalidation failed");
        }
    }

    internal static string BuildKey(string query, string equipmentId, string pipelineMode, int topK)
    {
        var raw = $"{query}|{equipmentId}|{pipelineMode}|{topK}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16];
        return CachePrefix + hash;
    }
}
