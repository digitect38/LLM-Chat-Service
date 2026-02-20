using System.Text.Json;
using FabCopilot.Contracts.Models;
using FabCopilot.RagService.Configuration;
using FabCopilot.RagService.Interfaces;
using FabCopilot.Redis.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FabCopilot.RagService.Services;

public sealed class RedisKnowledgeGraphStore : IKnowledgeGraphStore
{
    private const string EntityPrefix = "graph:entity:";
    private const string RelationPrefix = "graph:rel:";
    private const string IndexPrefix = "graph:idx:";

    private readonly ISessionStore _sessionStore;
    private readonly RagOptions _ragOptions;
    private readonly ILogger<RedisKnowledgeGraphStore> _logger;

    public RedisKnowledgeGraphStore(
        ISessionStore sessionStore,
        IOptions<RagOptions> ragOptions,
        ILogger<RedisKnowledgeGraphStore> logger)
    {
        _sessionStore = sessionStore;
        _ragOptions = ragOptions.Value;
        _logger = logger;
    }

    public async Task UpsertEntityAsync(GraphEntity entity, CancellationToken ct)
    {
        var key = $"{EntityPrefix}{entity.Name.ToLowerInvariant()}";
        await _sessionStore.SetAsync(key, entity, ct: ct);

        // Add to type index
        var indexKey = $"{IndexPrefix}{entity.Type.ToLowerInvariant()}";
        var index = await _sessionStore.GetAsync<HashSet<string>>(indexKey, ct) ?? [];
        index.Add(entity.Name.ToLowerInvariant());
        await _sessionStore.SetAsync(indexKey, index, ct: ct);

        _logger.LogDebug("Upserted entity: {Name} ({Type})", entity.Name, entity.Type);
    }

    public async Task UpsertRelationAsync(GraphRelation relation, CancellationToken ct)
    {
        var key = $"{RelationPrefix}{relation.SourceId.ToLowerInvariant()}:{relation.RelationType.ToLowerInvariant()}:{relation.TargetId.ToLowerInvariant()}";
        await _sessionStore.SetAsync(key, relation, ct: ct);

        // Store reverse index for traversal: source → list of relation keys
        var sourceRelKey = $"graph:src:{relation.SourceId.ToLowerInvariant()}";
        var sourceRels = await _sessionStore.GetAsync<List<string>>(sourceRelKey, ct) ?? [];
        if (!sourceRels.Contains(key))
        {
            sourceRels.Add(key);
            await _sessionStore.SetAsync(sourceRelKey, sourceRels, ct: ct);
        }

        _logger.LogDebug("Upserted relation: {Source} -[{Type}]-> {Target}",
            relation.SourceId, relation.RelationType, relation.TargetId);
    }

    public async Task<List<GraphEntity>> GetRelatedEntitiesAsync(
        string entityName, int maxDepth, CancellationToken ct)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<GraphEntity>();
        var queue = new Queue<(string Name, int Depth)>();
        queue.Enqueue((entityName.ToLowerInvariant(), 0));

        while (queue.Count > 0)
        {
            var (name, depth) = queue.Dequeue();
            if (!visited.Add(name) || depth > maxDepth)
                continue;

            // Get the entity itself
            var entity = await _sessionStore.GetAsync<GraphEntity>($"{EntityPrefix}{name}", ct);
            if (entity is not null)
            {
                result.Add(entity);
            }

            if (depth >= maxDepth)
                continue;

            // Get all relations from this entity
            var sourceRelKey = $"graph:src:{name}";
            var relationKeys = await _sessionStore.GetAsync<List<string>>(sourceRelKey, ct);
            if (relationKeys is null)
                continue;

            foreach (var relKey in relationKeys)
            {
                var relation = await _sessionStore.GetAsync<GraphRelation>(relKey, ct);
                if (relation is not null)
                {
                    queue.Enqueue((relation.TargetId.ToLowerInvariant(), depth + 1));
                }
            }
        }

        return result;
    }

    public async Task<string> BuildGraphContextAsync(
        string query, List<string> keywords, CancellationToken ct)
    {
        var maxDepth = _ragOptions.GraphMaxDepth;
        var allEntities = new List<GraphEntity>();

        // Find entities matching keywords
        foreach (var keyword in keywords)
        {
            var related = await GetRelatedEntitiesAsync(keyword, maxDepth, ct);
            allEntities.AddRange(related);
        }

        if (allEntities.Count == 0)
            return string.Empty;

        // Deduplicate by name
        var unique = allEntities
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        // Build context string
        var parts = new List<string> { "[관련 지식 그래프 엔티티]" };
        foreach (var entity in unique)
        {
            var props = entity.Properties.Count > 0
                ? $" ({string.Join(", ", entity.Properties.Select(p => $"{p.Key}: {p.Value}"))})"
                : "";
            parts.Add($"- {entity.Name} [{entity.Type}]{props}");
        }

        var context = string.Join("\n", parts);
        _logger.LogDebug("Built graph context with {Count} entities", unique.Count);
        return context;
    }
}
