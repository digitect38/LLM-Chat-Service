using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FabCopilot.Llm.Configuration;
using FabCopilot.Llm.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FabCopilot.Llm;

public sealed class OllamaEmbeddingClient : IEmbeddingClient
{
    private readonly IOptionsMonitor<OllamaOptions> _optionsMonitor;
    private readonly ILogger<OllamaEmbeddingClient> _logger;

    public OllamaEmbeddingClient(
        IOptionsMonitor<OllamaOptions> optionsMonitor,
        ILogger<OllamaEmbeddingClient> logger)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task<float[]> GetEmbeddingAsync(string text, bool isQuery = false, CancellationToken ct = default)
    {
        var options = _optionsMonitor.CurrentValue;

        // nomic-embed-text requires task-type prefixes for optimal asymmetric retrieval;
        // other models (e.g., snowflake-arctic-embed2) do not use these prefixes.
        var embeddingText = options.EmbeddingModel.StartsWith("nomic", StringComparison.OrdinalIgnoreCase)
            ? (isQuery ? "search_query: " + text : "search_document: " + text)
            : text;

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(options.BaseUrl),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };

        var payload = new OllamaEmbedPayload
        {
            Model = options.EmbeddingModel,
            Input = [embeddingText]
        };

        var response = await httpClient.PostAsJsonAsync("/api/embed", payload, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(ct);

        if (result?.Embeddings is { Length: > 0 })
        {
            var embedding = result.Embeddings[0];
            _logger.LogDebug("Ollama embedding generated. Model={Model}, Dim={Dim}, FirstVal={FirstVal}",
                options.EmbeddingModel, embedding.Length,
                embedding.Length > 0 ? embedding[0].ToString("F6") : "N/A");
            return embedding;
        }

        _logger.LogWarning("Ollama embedding returned empty result for model {Model}", options.EmbeddingModel);
        return [];
    }

    private sealed class OllamaEmbedPayload
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("input")]
        public string[] Input { get; set; } = [];
    }

    private sealed class OllamaEmbedResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("embeddings")]
        public float[][]? Embeddings { get; set; }
    }
}
