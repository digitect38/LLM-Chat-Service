using FabCopilot.VectorStore.Models;

namespace FabCopilot.VectorStore.Interfaces;

public interface IVectorStore
{
    Task UpsertAsync(string collection, string id, float[] vector,
        Dictionary<string, object> payload, CancellationToken ct = default);

    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(string collection,
        float[] queryVector, int topK = 5, Dictionary<string, object>? filter = null,
        CancellationToken ct = default);

    Task DeleteAsync(string collection, string id, CancellationToken ct = default);

    Task DeleteByDocumentIdAsync(string collection, string documentId, CancellationToken ct = default);

    Task EnsureCollectionAsync(string collection, int vectorSize, CancellationToken ct = default);
}
