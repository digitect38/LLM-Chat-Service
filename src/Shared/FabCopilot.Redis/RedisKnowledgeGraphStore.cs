using FabCopilot.Contracts.Configuration;
using FabCopilot.Contracts.Interfaces;
using FabCopilot.Contracts.Models;
using FabCopilot.Redis.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FabCopilot.Redis;

/// <summary>
/// Redis-backed knowledge graph store shared by RagService and KnowledgeService.
/// </summary>
public sealed class RedisKnowledgeGraphStore : IKnowledgeGraphStore
{
    private const string EntityPrefix = "graph:entity:";
    private const string RelationPrefix = "graph:rel:";
    private const string IndexPrefix = "graph:idx:";
    private const string KeywordPrefix = "graph:kw:";
    private const string AllEntitiesKey = "graph:all_entities";
    private const string AllRelationsKey = "graph:all_relations";

    private readonly ISessionStore _sessionStore;
    private readonly int _graphMaxDepth;
    private readonly ILogger<RedisKnowledgeGraphStore> _logger;

    public RedisKnowledgeGraphStore(
        ISessionStore sessionStore,
        IOptions<GraphOptions> graphOptions,
        ILogger<RedisKnowledgeGraphStore> logger)
    {
        _sessionStore = sessionStore;
        _graphMaxDepth = graphOptions.Value.GraphMaxDepth;
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

        // Add to global entity list
        var allEntities = await _sessionStore.GetAsync<HashSet<string>>(AllEntitiesKey, ct) ?? [];
        allEntities.Add(entity.Name.ToLowerInvariant());
        await _sessionStore.SetAsync(AllEntitiesKey, allEntities, ct: ct);

        // Add to keyword index: each word in entity name maps to the entity
        var normalizedName = entity.Name.ToLowerInvariant();
        var words = normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            if (word.Length < 2) continue;
            var kwKey = $"{KeywordPrefix}{word}";
            var entityNames = await _sessionStore.GetAsync<HashSet<string>>(kwKey, ct) ?? [];
            if (entityNames.Add(normalizedName))
                await _sessionStore.SetAsync(kwKey, entityNames, ct: ct);
        }

        _logger.LogDebug("Upserted entity: {Name} ({Type})", entity.Name, entity.Type);
    }

    public async Task UpsertRelationAsync(GraphRelation relation, CancellationToken ct)
    {
        var key = $"{RelationPrefix}{relation.SourceId.ToLowerInvariant()}:{relation.RelationType.ToLowerInvariant()}:{relation.TargetId.ToLowerInvariant()}";
        await _sessionStore.SetAsync(key, relation, ct: ct);

        // Forward index: source → list of relation keys
        var sourceRelKey = $"graph:src:{relation.SourceId.ToLowerInvariant()}";
        var sourceRels = await _sessionStore.GetAsync<List<string>>(sourceRelKey, ct) ?? [];
        if (!sourceRels.Contains(key))
        {
            sourceRels.Add(key);
            await _sessionStore.SetAsync(sourceRelKey, sourceRels, ct: ct);
        }

        // Reverse index: target → list of relation keys
        var targetRelKey = $"graph:tgt:{relation.TargetId.ToLowerInvariant()}";
        var targetRels = await _sessionStore.GetAsync<List<string>>(targetRelKey, ct) ?? [];
        if (!targetRels.Contains(key))
        {
            targetRels.Add(key);
            await _sessionStore.SetAsync(targetRelKey, targetRels, ct: ct);
        }

        // Track all relation keys for stats
        var allRelations = await _sessionStore.GetAsync<HashSet<string>>(AllRelationsKey, ct) ?? [];
        allRelations.Add(key);
        await _sessionStore.SetAsync(AllRelationsKey, allRelations, ct: ct);

        _logger.LogDebug("Upserted relation: {Source} -[{Type}]-> {Target}",
            relation.SourceId, relation.RelationType, relation.TargetId);
    }

    public async Task<GraphEntity?> GetEntityAsync(string name, CancellationToken ct)
    {
        return await _sessionStore.GetAsync<GraphEntity>($"{EntityPrefix}{name.ToLowerInvariant()}", ct);
    }

    public async Task<List<GraphEntity>> ListEntitiesAsync(string? type, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(type))
        {
            var indexKey = $"{IndexPrefix}{type.ToLowerInvariant()}";
            var names = await _sessionStore.GetAsync<HashSet<string>>(indexKey, ct) ?? [];
            var entities = new List<GraphEntity>();
            foreach (var name in names)
            {
                var entity = await _sessionStore.GetAsync<GraphEntity>($"{EntityPrefix}{name}", ct);
                if (entity is not null)
                    entities.Add(entity);
            }
            return entities;
        }

        var allNames = await _sessionStore.GetAsync<HashSet<string>>(AllEntitiesKey, ct) ?? [];
        var result = new List<GraphEntity>();
        foreach (var name in allNames)
        {
            var entity = await _sessionStore.GetAsync<GraphEntity>($"{EntityPrefix}{name}", ct);
            if (entity is not null)
                result.Add(entity);
        }
        return result;
    }

    public async Task DeleteEntityAsync(string name, CancellationToken ct)
    {
        var normalizedName = name.ToLowerInvariant();
        var entity = await _sessionStore.GetAsync<GraphEntity>($"{EntityPrefix}{normalizedName}", ct);
        if (entity is null) return;

        var indexKey = $"{IndexPrefix}{entity.Type.ToLowerInvariant()}";
        var index = await _sessionStore.GetAsync<HashSet<string>>(indexKey, ct);
        if (index is not null)
        {
            index.Remove(normalizedName);
            await _sessionStore.SetAsync(indexKey, index, ct: ct);
        }

        var allEntities = await _sessionStore.GetAsync<HashSet<string>>(AllEntitiesKey, ct);
        if (allEntities is not null)
        {
            allEntities.Remove(normalizedName);
            await _sessionStore.SetAsync(AllEntitiesKey, allEntities, ct: ct);
        }

        await _sessionStore.DeleteAsync($"{EntityPrefix}{normalizedName}", ct);
        _logger.LogDebug("Deleted entity: {Name}", name);
    }

    public async Task<GraphStats> GetStatsAsync(CancellationToken ct)
    {
        var allEntityNames = await _sessionStore.GetAsync<HashSet<string>>(AllEntitiesKey, ct) ?? [];
        var allRelationKeys = await _sessionStore.GetAsync<HashSet<string>>(AllRelationsKey, ct) ?? [];

        var entitiesByType = new Dictionary<string, int>();
        foreach (var name in allEntityNames)
        {
            var entity = await _sessionStore.GetAsync<GraphEntity>($"{EntityPrefix}{name}", ct);
            if (entity is not null)
            {
                var typeLower = entity.Type.ToLowerInvariant();
                entitiesByType.TryGetValue(typeLower, out var count);
                entitiesByType[typeLower] = count + 1;
            }
        }

        return new GraphStats
        {
            EntityCount = allEntityNames.Count,
            RelationCount = allRelationKeys.Count,
            EntitiesByType = entitiesByType
        };
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

            var entity = await _sessionStore.GetAsync<GraphEntity>($"{EntityPrefix}{name}", ct);
            if (entity is not null)
                result.Add(entity);

            if (depth >= maxDepth)
                continue;

            // Forward: source → target
            var sourceRelKeys = await _sessionStore.GetAsync<List<string>>($"graph:src:{name}", ct);
            if (sourceRelKeys is not null)
            {
                foreach (var relKey in sourceRelKeys)
                {
                    var relation = await _sessionStore.GetAsync<GraphRelation>(relKey, ct);
                    if (relation is not null)
                        queue.Enqueue((relation.TargetId.ToLowerInvariant(), depth + 1));
                }
            }

            // Reverse: target → source
            var targetRelKeys = await _sessionStore.GetAsync<List<string>>($"graph:tgt:{name}", ct);
            if (targetRelKeys is not null)
            {
                foreach (var relKey in targetRelKeys)
                {
                    var relation = await _sessionStore.GetAsync<GraphRelation>(relKey, ct);
                    if (relation is not null)
                        queue.Enqueue((relation.SourceId.ToLowerInvariant(), depth + 1));
                }
            }
        }

        return result;
    }

    public async Task<List<GraphRelation>> GetRelatedRelationsAsync(
        string entityName, int maxDepth, CancellationToken ct)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var collectedRelations = new List<GraphRelation>();
        var seenRelationKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Name, int Depth)>();
        queue.Enqueue((entityName.ToLowerInvariant(), 0));

        while (queue.Count > 0)
        {
            var (name, depth) = queue.Dequeue();
            if (!visited.Add(name) || depth > maxDepth)
                continue;

            if (depth >= maxDepth)
                continue;

            var sourceRelKeys = await _sessionStore.GetAsync<List<string>>($"graph:src:{name}", ct);
            if (sourceRelKeys is not null)
            {
                foreach (var relKey in sourceRelKeys)
                {
                    if (!seenRelationKeys.Add(relKey)) continue;
                    var relation = await _sessionStore.GetAsync<GraphRelation>(relKey, ct);
                    if (relation is not null)
                    {
                        collectedRelations.Add(relation);
                        queue.Enqueue((relation.TargetId.ToLowerInvariant(), depth + 1));
                    }
                }
            }

            var targetRelKeys = await _sessionStore.GetAsync<List<string>>($"graph:tgt:{name}", ct);
            if (targetRelKeys is not null)
            {
                foreach (var relKey in targetRelKeys)
                {
                    if (!seenRelationKeys.Add(relKey)) continue;
                    var relation = await _sessionStore.GetAsync<GraphRelation>(relKey, ct);
                    if (relation is not null)
                    {
                        collectedRelations.Add(relation);
                        queue.Enqueue((relation.SourceId.ToLowerInvariant(), depth + 1));
                    }
                }
            }
        }

        return collectedRelations;
    }

    /// <summary>
    /// Rebuilds the keyword index for all existing entities.
    /// </summary>
    public async Task RebuildKeywordIndexAsync(CancellationToken ct)
    {
        var allEntityNames = await _sessionStore.GetAsync<HashSet<string>>(AllEntitiesKey, ct) ?? [];
        var indexedCount = 0;

        foreach (var entityName in allEntityNames)
        {
            var words = entityName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                if (word.Length < 2) continue;
                var kwKey = $"{KeywordPrefix}{word}";
                var names = await _sessionStore.GetAsync<HashSet<string>>(kwKey, ct) ?? [];
                if (names.Add(entityName))
                {
                    await _sessionStore.SetAsync(kwKey, names, ct: ct);
                    indexedCount++;
                }
            }
        }

        _logger.LogInformation(
            "Keyword index rebuilt. Entities={EntityCount}, IndexEntries={IndexCount}",
            allEntityNames.Count, indexedCount);
    }

    /// <summary>
    /// Resolves keywords to actual entity names via keyword index and exact match.
    /// </summary>
    public async Task<List<string>> ResolveKeywordsToEntitiesAsync(
        List<string> keywords, CancellationToken ct)
    {
        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var keyword in keywords)
        {
            var normalized = keyword.ToLowerInvariant();

            var entity = await _sessionStore.GetAsync<GraphEntity>($"{EntityPrefix}{normalized}", ct);
            if (entity is not null)
                resolved.Add(normalized);

            var kwKey = $"{KeywordPrefix}{normalized}";
            var entityNames = await _sessionStore.GetAsync<HashSet<string>>(kwKey, ct);
            if (entityNames is not null)
            {
                foreach (var name in entityNames)
                    resolved.Add(name);
            }
        }
        return resolved.ToList();
    }

    public async Task<string> BuildGraphContextAsync(
        string query, List<string> keywords, CancellationToken ct)
    {
        var maxDepth = _graphMaxDepth;
        var allEntities = new List<GraphEntity>();
        var allRelations = new List<GraphRelation>();

        // Resolve keywords to actual entity names via keyword index (cap at 10)
        var resolvedEntityNames = await ResolveKeywordsToEntitiesAsync(keywords, ct);

        _logger.LogInformation(
            "Resolved {KeywordCount} keywords to {EntityNameCount} entity names: [{EntityNames}]",
            keywords.Count, resolvedEntityNames.Count,
            string.Join(", ", resolvedEntityNames.Take(10)));

        foreach (var entityName in resolvedEntityNames.Take(10))
        {
            var related = await GetRelatedEntitiesAsync(entityName, maxDepth, ct);
            allEntities.AddRange(related);

            var relations = await GetRelatedRelationsAsync(entityName, maxDepth, ct);
            allRelations.AddRange(relations);
        }

        if (allEntities.Count == 0 && allRelations.Count == 0)
            return string.Empty;

        const int maxEntities = 20;
        const int maxRelations = 30;

        var uniqueEntities = allEntities
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(maxEntities)
            .ToList();

        var uniqueRelations = allRelations
            .GroupBy(r => $"{r.SourceId}:{r.RelationType}:{r.TargetId}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(maxRelations)
            .ToList();

        var parts = new List<string> { "[관련 지식 그래프]" };

        if (uniqueEntities.Count > 0)
        {
            parts.Add("");
            parts.Add("엔티티:");
            foreach (var entity in uniqueEntities)
            {
                var props = entity.Properties.Count > 0
                    ? $" ({string.Join(", ", entity.Properties.Select(p => $"{p.Key}: {p.Value}"))})"
                    : "";
                parts.Add($"- {entity.Name} [{entity.Type}]{props}");
            }
        }

        if (uniqueRelations.Count > 0)
        {
            parts.Add("");
            parts.Add("관계:");
            foreach (var rel in uniqueRelations)
            {
                parts.Add($"- {rel.SourceId} -[{rel.RelationType}]-> {rel.TargetId}");
            }
        }

        var context = string.Join("\n", parts);
        _logger.LogDebug("Built graph context with {EntityCount} entities and {RelationCount} relations",
            uniqueEntities.Count, uniqueRelations.Count);
        return context;
    }
}
