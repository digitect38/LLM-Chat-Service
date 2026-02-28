using System.Runtime.CompilerServices;
using FabCopilot.Llm.Configuration;
using FabCopilot.Llm.Interfaces;
using FabCopilot.Llm.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FabCopilot.Llm;

/// <summary>
/// Resolves the active ILlmClient based on current provider configuration.
/// Supports hot-reload: when Llm:Provider changes in appsettings.json,
/// new requests automatically use the new provider.
/// In-flight requests continue with the previously resolved client.
/// </summary>
public sealed class LlmClientResolver : ILlmClient, IDisposable
{
    private readonly OllamaLlmClient _ollamaClient;
    private readonly TgiLlmClient _tgiClient;
    private readonly ILogger<LlmClientResolver> _logger;
    private volatile string _currentProvider;
    private readonly IDisposable? _optionsChangeToken;

    public LlmClientResolver(
        OllamaLlmClient ollamaClient,
        TgiLlmClient tgiClient,
        IOptionsMonitor<LlmProviderOptions> providerMonitor,
        ILogger<LlmClientResolver> logger)
    {
        _ollamaClient = ollamaClient;
        _tgiClient = tgiClient;
        _logger = logger;
        _currentProvider = providerMonitor.CurrentValue.Provider;

        _optionsChangeToken = providerMonitor.OnChange(opts =>
        {
            var newProvider = opts.Provider;
            if (!string.Equals(_currentProvider, newProvider, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "LLM provider hot-reloaded: {OldProvider} → {NewProvider}. New requests will use {NewProvider}.",
                    _currentProvider, newProvider, newProvider);
                _currentProvider = newProvider;
            }
        });

        _logger.LogInformation("LLM client resolver initialized with provider: {Provider}", _currentProvider);
    }

    public string CurrentProvider => _currentProvider;

    private ILlmClient Resolve()
    {
        return _currentProvider.Equals("Tgi", StringComparison.OrdinalIgnoreCase)
            ? _tgiClient
            : _ollamaClient;
    }

    public IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<LlmChatMessage> messages,
        LlmOptions? options = null,
        CancellationToken ct = default)
    {
        // Capture the client at call time — in-flight uses this instance even if provider changes mid-stream
        var client = Resolve();
        return client.StreamChatAsync(messages, options, ct);
    }

    public Task<string> CompleteChatAsync(
        IReadOnlyList<LlmChatMessage> messages,
        LlmOptions? options = null,
        CancellationToken ct = default)
    {
        var client = Resolve();
        return client.CompleteChatAsync(messages, options, ct);
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
