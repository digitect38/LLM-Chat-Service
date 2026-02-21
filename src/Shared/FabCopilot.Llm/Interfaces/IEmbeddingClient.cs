namespace FabCopilot.Llm.Interfaces;

public interface IEmbeddingClient
{
    Task<float[]> GetEmbeddingAsync(string text, bool isQuery = false, CancellationToken ct = default);
}
