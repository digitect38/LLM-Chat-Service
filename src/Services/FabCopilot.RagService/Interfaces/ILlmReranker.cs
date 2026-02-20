using FabCopilot.VectorStore.Models;

namespace FabCopilot.RagService.Interfaces;

public interface ILlmReranker
{
    Task<List<VectorSearchResult>> RerankAsync(
        string query,
        IReadOnlyList<VectorSearchResult> candidates,
        int topK,
        CancellationToken ct);
}
