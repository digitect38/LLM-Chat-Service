using FabCopilot.Llm.Configuration;
using FabCopilot.Llm.Interfaces;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models;

namespace FabCopilot.Llm;

public sealed class OllamaEmbeddingClient : IEmbeddingClient
{
    private readonly IOptionsMonitor<OllamaOptions> _optionsMonitor;

    public OllamaEmbeddingClient(IOptionsMonitor<OllamaOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public async Task<float[]> GetEmbeddingAsync(string text, bool isQuery = false, CancellationToken ct = default)
    {
        var options = _optionsMonitor.CurrentValue;

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(options.BaseUrl),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };
        var ollama = new OllamaApiClient(httpClient);

        // nomic-embed-text requires task-type prefixes for optimal asymmetric retrieval;
        // other models (e.g., snowflake-arctic-embed2) do not use these prefixes.
        var embeddingText = options.EmbeddingModel.StartsWith("nomic", StringComparison.OrdinalIgnoreCase)
            ? (isQuery ? "search_query: " + text : "search_document: " + text)
            : text;

        var response = await ollama.EmbedAsync(
            new EmbedRequest
            {
                Model = options.EmbeddingModel,
                Input = [embeddingText]
            },
            ct);

        if (response?.Embeddings is { Count: > 0 })
        {
            var embedding = response.Embeddings[0];
            return embedding.Select(d => (float)d).ToArray();
        }

        return [];
    }
}
