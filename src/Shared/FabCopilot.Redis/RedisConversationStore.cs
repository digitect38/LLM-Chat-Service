using System.Text.Json;
using FabCopilot.Contracts.Models;
using FabCopilot.Redis.Configuration;
using FabCopilot.Redis.Interfaces;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace FabCopilot.Redis;

public sealed class RedisConversationStore : IConversationStore
{
    private readonly IDatabase _db;
    private readonly string _prefix;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public RedisConversationStore(IConnectionMultiplexer redis, IOptions<RedisOptions> options)
    {
        var opts = options.Value;
        _db = redis.GetDatabase(opts.DefaultDatabase);
        _prefix = opts.KeyPrefix;
    }

    public async Task<Conversation?> GetAsync(string conversationId, CancellationToken ct = default)
    {
        var key = ConversationKey(conversationId);
        var json = await _db.StringGetAsync(key).ConfigureAwait(false);

        if (json.IsNullOrEmpty)
            return null;

        return JsonSerializer.Deserialize<Conversation>(json!, JsonOptions);
    }

    public async Task SaveAsync(Conversation conversation, CancellationToken ct = default)
    {
        var key = ConversationKey(conversation.ConversationId);
        var json = JsonSerializer.Serialize(conversation, JsonOptions);

        await _db.StringSetAsync(key, json).ConfigureAwait(false);

        // Maintain the equipment sorted set for lookups by equipment
        if (!string.IsNullOrEmpty(conversation.EquipmentId))
        {
            var equipKey = EquipmentKey(conversation.EquipmentId);
            var score = conversation.LastUpdatedAt.UtcTicks;
            await _db.SortedSetAddAsync(equipKey, conversation.ConversationId, score).ConfigureAwait(false);
        }
    }

    public async Task AppendMessageAsync(string conversationId, ChatMessage message, CancellationToken ct = default)
    {
        var conversation = await GetAsync(conversationId, ct).ConfigureAwait(false);

        if (conversation is null)
            throw new KeyNotFoundException($"Conversation '{conversationId}' not found.");

        conversation.Messages.Add(message);
        conversation.LastUpdatedAt = DateTimeOffset.UtcNow;

        await SaveAsync(conversation, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Conversation>> GetByEquipmentAsync(
        string equipmentId,
        int limit = 20,
        CancellationToken ct = default)
    {
        var equipKey = EquipmentKey(equipmentId);

        // Get conversation IDs ordered by most recent first
        var members = await _db.SortedSetRangeByRankAsync(
            equipKey,
            start: 0,
            stop: limit - 1,
            order: Order.Descending).ConfigureAwait(false);

        var conversations = new List<Conversation>(members.Length);

        foreach (var member in members)
        {
            var conversation = await GetAsync(member!, ct).ConfigureAwait(false);
            if (conversation is not null)
                conversations.Add(conversation);
        }

        return conversations.AsReadOnly();
    }

    private string ConversationKey(string conversationId) => $"{_prefix}conv:{conversationId}";
    private string EquipmentKey(string equipmentId) => $"{_prefix}equip:{equipmentId}:conversations";
}
