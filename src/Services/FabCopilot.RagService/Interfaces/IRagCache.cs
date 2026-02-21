using FabCopilot.Contracts.Messages;

namespace FabCopilot.RagService.Interfaces;

public interface IRagCache
{
    /// <summary>
    /// Tries to get a cached RAG response for the given request parameters.
    /// </summary>
    Task<RagResponse?> GetAsync(string query, string equipmentId, string pipelineMode, int topK, CancellationToken ct = default);

    /// <summary>
    /// Caches a RAG response.
    /// </summary>
    Task SetAsync(string query, string equipmentId, string pipelineMode, int topK, RagResponse response, CancellationToken ct = default);

    /// <summary>
    /// Invalidates all cached RAG responses (e.g., after document changes).
    /// </summary>
    Task InvalidateAllAsync(CancellationToken ct = default);
}
