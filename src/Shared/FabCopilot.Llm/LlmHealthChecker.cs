using System.Diagnostics;
using FabCopilot.Llm.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FabCopilot.Llm;

/// <summary>
/// Background service that monitors LLM server health via periodic heartbeats.
/// Tracks primary and fallback server availability and inference latency.
/// </summary>
public sealed class LlmHealthChecker : BackgroundService
{
    private readonly IOptionsMonitor<OllamaOptions> _ollamaOptions;
    private readonly IOptionsMonitor<TgiOptions> _tgiOptions;
    private readonly IOptionsMonitor<LlmProviderOptions> _providerOptions;
    private readonly IOptionsMonitor<FallbackServerOptions> _fallbackOptions;
    private readonly ILogger<LlmHealthChecker> _logger;

    private volatile ServerHealthState _primaryState = new();
    private volatile ServerHealthState _fallbackState = new();

    public LlmHealthChecker(
        IOptionsMonitor<OllamaOptions> ollamaOptions,
        IOptionsMonitor<TgiOptions> tgiOptions,
        IOptionsMonitor<LlmProviderOptions> providerOptions,
        IOptionsMonitor<FallbackServerOptions> fallbackOptions,
        ILogger<LlmHealthChecker> logger)
    {
        _ollamaOptions = ollamaOptions;
        _tgiOptions = tgiOptions;
        _providerOptions = providerOptions;
        _fallbackOptions = fallbackOptions;
        _logger = logger;
    }

    public bool IsPrimaryHealthy => _primaryState.IsHealthy;
    public bool IsFallbackHealthy => _fallbackState.IsHealthy;
    public double PrimaryLatencyMs => _primaryState.LastLatencyMs;
    public double FallbackLatencyMs => _fallbackState.LastLatencyMs;
    public DateTimeOffset? PrimaryLastSuccess => _primaryState.LastSuccess;
    public DateTimeOffset? FallbackLastSuccess => _fallbackState.LastSuccess;

    /// <summary>
    /// Returns true if primary has been unresponsive longer than the failover threshold.
    /// </summary>
    public bool ShouldFailover
    {
        get
        {
            var fallbackOpts = _fallbackOptions.CurrentValue;
            if (!fallbackOpts.Enabled) return false;
            if (_primaryState.IsHealthy) return false;
            if (_primaryState.LastSuccess is null) return true;

            var elapsed = DateTimeOffset.UtcNow - _primaryState.LastSuccess.Value;
            return elapsed.TotalSeconds >= fallbackOpts.FailoverThresholdSeconds;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LLM Health Checker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var fallbackOpts = _fallbackOptions.CurrentValue;
            var interval = TimeSpan.FromSeconds(
                fallbackOpts.Enabled ? fallbackOpts.HealthCheckIntervalSeconds : 30);

            try
            {
                await CheckPrimaryHealthAsync(stoppingToken);

                if (fallbackOpts.Enabled && !string.IsNullOrEmpty(fallbackOpts.BaseUrl))
                {
                    await CheckServerHealthAsync(fallbackOpts.BaseUrl, fallbackOpts.Provider, _fallbackState, "Fallback", stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check cycle failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task CheckPrimaryHealthAsync(CancellationToken ct)
    {
        var provider = _providerOptions.CurrentValue.Provider;
        var baseUrl = provider.Equals("Tgi", StringComparison.OrdinalIgnoreCase)
            ? _tgiOptions.CurrentValue.BaseUrl
            : _ollamaOptions.CurrentValue.BaseUrl;

        await CheckServerHealthAsync(baseUrl, provider, _primaryState, "Primary", ct);
    }

    private async Task CheckServerHealthAsync(string baseUrl, string provider, ServerHealthState state, string label, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            // Ollama: GET /api/tags, TGI: GET /health
            var healthUrl = provider.Equals("Tgi", StringComparison.OrdinalIgnoreCase)
                ? $"{baseUrl.TrimEnd('/')}/health"
                : $"{baseUrl.TrimEnd('/')}/api/tags";

            var response = await httpClient.GetAsync(healthUrl, ct);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                var wasUnhealthy = !state.IsHealthy;
                state.IsHealthy = true;
                state.LastSuccess = DateTimeOffset.UtcNow;
                state.LastLatencyMs = sw.Elapsed.TotalMilliseconds;
                state.ConsecutiveFailures = 0;

                if (wasUnhealthy)
                {
                    _logger.LogInformation("{Label} LLM server recovered at {BaseUrl} (latency: {Latency:F0}ms)",
                        label, baseUrl, sw.Elapsed.TotalMilliseconds);
                }
            }
            else
            {
                state.ConsecutiveFailures++;
                state.IsHealthy = false;
                _logger.LogWarning("{Label} LLM server unhealthy at {BaseUrl}: HTTP {StatusCode} (failures: {Count})",
                    label, baseUrl, (int)response.StatusCode, state.ConsecutiveFailures);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            state.ConsecutiveFailures++;
            state.IsHealthy = false;
            _logger.LogWarning("{Label} LLM server unreachable at {BaseUrl}: {Error} (failures: {Count})",
                label, baseUrl, ex.Message, state.ConsecutiveFailures);
        }
    }

    private class ServerHealthState
    {
        public volatile bool IsHealthy = true;
        public DateTimeOffset? LastSuccess;
        public double LastLatencyMs;
        public int ConsecutiveFailures;
    }
}
