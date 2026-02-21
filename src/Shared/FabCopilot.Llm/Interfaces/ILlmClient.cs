using FabCopilot.Llm.Models;

namespace FabCopilot.Llm.Interfaces;

public interface ILlmClient
{
    IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<LlmChatMessage> messages,
        LlmOptions? options = null,
        CancellationToken ct = default);

    Task<string> CompleteChatAsync(
        IReadOnlyList<LlmChatMessage> messages,
        LlmOptions? options = null,
        CancellationToken ct = default);

    Task<float[]> GetEmbeddingAsync(string text, bool isQuery = false, CancellationToken ct = default);
}
