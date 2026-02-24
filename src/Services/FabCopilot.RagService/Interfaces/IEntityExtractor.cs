using FabCopilot.Contracts.Models;

namespace FabCopilot.RagService.Interfaces;

public interface IEntityExtractor
{
    Task<List<GraphEntity>> ExtractEntitiesAsync(string text, CancellationToken ct);
    Task<List<GraphRelation>> ExtractRelationsAsync(string text, List<GraphEntity> entities, CancellationToken ct);

    /// <summary>
    /// Batch extracts entities and relations from multiple chunks in fewer LLM calls.
    /// Chunks are concatenated up to ~1800 chars per batch to reduce LLM round-trips.
    /// </summary>
    Task<(List<GraphEntity> Entities, List<GraphRelation> Relations)> ExtractFromBatchAsync(
        List<string> chunks, CancellationToken ct);
}
