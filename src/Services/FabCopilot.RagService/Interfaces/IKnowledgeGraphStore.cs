using FabCopilot.Contracts.Models;

namespace FabCopilot.RagService.Interfaces;

/// <summary>
/// RagService-local interface for knowledge graph store.
/// Mirrors the shared IKnowledgeGraphStore in Contracts for backward compatibility.
/// The RagService implementation (RedisKnowledgeGraphStore) implements this interface.
/// </summary>
public interface IKnowledgeGraphStore
{
    Task UpsertEntityAsync(GraphEntity entity, CancellationToken ct);
    Task UpsertRelationAsync(GraphRelation relation, CancellationToken ct);
    Task<GraphEntity?> GetEntityAsync(string name, CancellationToken ct);
    Task<List<GraphEntity>> ListEntitiesAsync(string? type, CancellationToken ct);
    Task DeleteEntityAsync(string name, CancellationToken ct);
    Task<GraphStats> GetStatsAsync(CancellationToken ct);
    Task<List<GraphEntity>> GetRelatedEntitiesAsync(string entityName, int maxDepth, CancellationToken ct);
    Task<List<GraphRelation>> GetRelatedRelationsAsync(string entityName, int maxDepth, CancellationToken ct);
    Task<string> BuildGraphContextAsync(string query, List<string> keywords, CancellationToken ct);
    Task RebuildKeywordIndexAsync(CancellationToken ct);
}
