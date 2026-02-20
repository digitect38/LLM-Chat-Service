using FabCopilot.Contracts.Models;

namespace FabCopilot.RagService.Interfaces;

public interface IEntityExtractor
{
    Task<List<GraphEntity>> ExtractEntitiesAsync(string text, CancellationToken ct);
    Task<List<GraphRelation>> ExtractRelationsAsync(string text, List<GraphEntity> entities, CancellationToken ct);
}
