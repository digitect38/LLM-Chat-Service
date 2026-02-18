using System.Text.Json;
using FabCopilot.Redis.Configuration;
using FabCopilot.Redis.Interfaces;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace FabCopilot.Redis;

public sealed class RedisSessionStore : ISessionStore
{
    private readonly IDatabase _db;
    private readonly string _prefix;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public RedisSessionStore(IConnectionMultiplexer redis, IOptions<RedisOptions> options)
    {
        var opts = options.Value;
        _db = redis.GetDatabase(opts.DefaultDatabase);
        _prefix = opts.KeyPrefix;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var redisKey = SessionKey(key);
        var json = await _db.StringGetAsync(redisKey).ConfigureAwait(false);

        if (json.IsNullOrEmpty)
            return default;

        return JsonSerializer.Deserialize<T>(json!, JsonOptions);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var redisKey = SessionKey(key);
        var json = JsonSerializer.Serialize(value, JsonOptions);

        await _db.StringSetAsync(redisKey, json, expiry).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var redisKey = SessionKey(key);
        await _db.KeyDeleteAsync(redisKey).ConfigureAwait(false);
    }

    private string SessionKey(string key) => $"{_prefix}session:{key}";
}
