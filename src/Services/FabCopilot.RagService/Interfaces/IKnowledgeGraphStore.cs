using FabCopilot.Contracts.Models;

namespace FabCopilot.RagService.Interfaces;

public interface IKnowledgeGraphStore
{
    Task UpsertEntityAsync(GraphEntity entity, CancellationToken ct);
    Task UpsertRelationAsync(GraphRelation relation, CancellationToken ct);
    Task<List<GraphEntity>> GetRelatedEntitiesAsync(string entityName, int maxDepth, CancellationToken ct);
    Task<string> BuildGraphContextAsync(string query, List<string> keywords, CancellationToken ct);
}
