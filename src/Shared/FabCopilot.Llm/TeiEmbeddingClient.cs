using System.Net.Http.Json;
using System.Text.Json;
using FabCopilot.Llm.Configuration;
using FabCopilot.Llm.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FabCopilot.Llm;

public sealed class TeiEmbeddingClient : IEmbeddingClient
{
    private readonly IOptionsMonitor<TeiOptions> _optionsMonitor;
    private readonly ILogger<TeiEmbeddingClient> _logger;

    public TeiEmbeddingClient(IOptionsMonitor<TeiOptions> optionsMonitor, ILogger<TeiEmbeddingClient> logger)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task<float[]> GetEmbeddingAsync(string text, bool isQuery = false, CancellationToken ct = default)
    {
        var options = _optionsMonitor.CurrentValue;

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(options.BaseUrl),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };

        var payload = new { inputs = text };

        _logger.LogDebug("Requesting TEI embedding from {BaseUrl} with model {Model}",
            options.BaseUrl, options.EmbeddingModel);

        var response = await httpClient.PostAsJsonAsync("/embed", payload, ct);
        response.EnsureSuccessStatusCode();

        // TEI returns [[float, ...]] — array of arrays
        var embeddings = await response.Content.ReadFromJsonAsync<float[][]>(ct);

        if (embeddings is { Length: > 0 })
        {
            return embeddings[0];
        }

        _logger.LogWarning("TEI returned empty embeddings for input text");
        return [];
    }
}
