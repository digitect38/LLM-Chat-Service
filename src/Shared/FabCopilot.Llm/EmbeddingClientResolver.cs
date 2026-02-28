using FabCopilot.Llm.Configuration;
using FabCopilot.Llm.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FabCopilot.Llm;

/// <summary>
/// Resolves the active IEmbeddingClient based on current provider configuration.
/// Supports hot-reload: when Embedding:Provider changes in appsettings.json,
/// new requests automatically use the new provider.
/// Logs a re-indexing warning when the embedding provider changes,
/// since existing vectors were generated with the previous model.
/// </summary>
public sealed class EmbeddingClientResolver : IEmbeddingClient, IDisposable
{
    private readonly OllamaEmbeddingClient _ollamaClient;
    private readonly TeiEmbeddingClient _teiClient;
    private readonly ILogger<EmbeddingClientResolver> _logger;
    private volatile string _currentProvider;
    private readonly IDisposable? _optionsChangeToken;

    public EmbeddingClientResolver(
        OllamaEmbeddingClient ollamaClient,
        TeiEmbeddingClient teiClient,
        IOptionsMonitor<EmbeddingProviderOptions> providerMonitor,
        ILogger<EmbeddingClientResolver> logger)
    {
        _ollamaClient = ollamaClient;
        _teiClient = teiClient;
        _logger = logger;
        _currentProvider = providerMonitor.CurrentValue.Provider;

        _optionsChangeToken = providerMonitor.OnChange(opts =>
        {
            var newProvider = opts.Provider;
            if (!string.Equals(_currentProvider, newProvider, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Embedding provider hot-reloaded: {OldProvider} → {NewProvider}. " +
                    "⚠ Existing vector indices were built with the previous model. " +
                    "Re-indexing is required for consistent search quality.",
                    _currentProvider, newProvider);
                _currentProvider = newProvider;
            }
        });

        _logger.LogInformation("Embedding client resolver initialized with provider: {Provider}", _currentProvider);
    }

    public string CurrentProvider => _currentProvider;

    private IEmbeddingClient Resolve()
    {
        return _currentProvider.Equals("Tei", StringComparison.OrdinalIgnoreCase)
            ? _teiClient
            : _ollamaClient;
    }

    public Task<float[]> GetEmbeddingAsync(string text, bool isQuery = false, CancellationToken ct = default)
    {
        var client = Resolve();
        return client.GetEmbeddingAsync(text, isQuery, ct);
    }

    public void Dispose()
    {
        _optionsChangeToken?.Dispose();
    }
}
