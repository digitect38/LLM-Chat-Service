using FabCopilot.Contracts.Models;

namespace FabCopilot.Contracts.Interfaces;

/// <summary>
/// Shared interface for knowledge graph storage.
/// Used by both RagService (graph context building) and KnowledgeService (REST API).
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
}
