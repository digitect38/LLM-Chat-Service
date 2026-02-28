using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using FabCopilot.Llm.Configuration;
using FabCopilot.Llm.Interfaces;
using FabCopilot.Llm.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;

namespace FabCopilot.Llm;

/// <summary>
/// Decorates the primary ILlmClient with automatic fallback to a secondary server
/// when the primary is determined unhealthy by LlmHealthChecker.
/// Also supports SLM conditional failover when both primary and fallback are unavailable.
/// </summary>
public sealed class FallbackLlmClient : ILlmClient
{
    private readonly ILlmClient _primaryClient;
    private readonly LlmHealthChecker _healthChecker;
    private readonly IOptionsMonitor<FallbackServerOptions> _fallbackOptions;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly ILogger<FallbackLlmClient> _logger;

    public FallbackLlmClient(
        ILlmClient primaryClient,
        LlmHealthChecker healthChecker,
        IOptionsMonitor<FallbackServerOptions> fallbackOptions,
        IEmbeddingClient embeddingClient,
        ILogger<FallbackLlmClient> logger)
    {
        _primaryClient = primaryClient;
        _healthChecker = healthChecker;
        _fallbackOptions = fallbackOptions;
        _embeddingClient = embeddingClient;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<LlmChatMessage> messages,
        LlmOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var fallbackOpts = _fallbackOptions.CurrentValue;

        // If primary is healthy, try it first
        if (!_healthChecker.ShouldFailover)
        {
            var channel = Channel.CreateUnbounded<string>();
            var primaryFailed = false;

            // Produce tokens in a background task to avoid yield-in-try-catch
            var producerTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var token in _primaryClient.StreamChatAsync(messages, options, ct))
                    {
                        await channel.Writer.WriteAsync(token, ct);
                    }
                    channel.Writer.Complete();
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
                {
                    _logger.LogWarning(ex, "Primary LLM failed mid-stream, attempting fallback");
                    primaryFailed = true;
                    channel.Writer.Complete(ex);
                }
                catch (Exception ex)
                {
                    channel.Writer.Complete(ex);
                }
            }, ct);

            // Read tokens from channel
            var reader = channel.Reader;
            while (true)
            {
                try
                {
                    if (!await reader.WaitToReadAsync(ct)) break;
                }
                catch (HttpRequestException) { primaryFailed = true; break; }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested) { primaryFailed = true; break; }
                catch (TimeoutException) { primaryFailed = true; break; }
                catch (ChannelClosedException) { break; }

                while (reader.TryRead(out var token))
                {
                    yield return token;
                }
            }

            await producerTask;

            if (!primaryFailed) yield break;
        }
        else
        {
            _logger.LogWarning("Primary LLM unhealthy, routing to fallback server");
        }

        // Try fallback server
        if (fallbackOpts.Enabled && !string.IsNullOrEmpty(fallbackOpts.BaseUrl) && _healthChecker.IsFallbackHealthy)
        {
            _logger.LogInformation("Using fallback LLM server at {BaseUrl}", fallbackOpts.BaseUrl);

            await foreach (var token in StreamFromFallbackAsync(fallbackOpts, messages, options, ct))
            {
                yield return token;
            }
            yield break;
        }

        // SLM conditional failover (last resort)
        if (!string.IsNullOrEmpty(fallbackOpts.SlmFallbackModel))
        {
            _logger.LogWarning("Both primary and fallback LLM servers unavailable. Using SLM mode with model: {Model}",
                fallbackOpts.SlmFallbackModel);

            yield return "⚠️ 기본 LLM 서버에 연결할 수 없어 경량 모델(SLM)로 응답합니다. 답변 품질이 제한될 수 있습니다.\n\n";

            var slmOptions = new LlmOptions
            {
                Model = fallbackOpts.SlmFallbackModel,
                Temperature = options?.Temperature ?? 0.1f,
                MaxTokens = options?.MaxTokens ?? 1024
            };

            await foreach (var token in _primaryClient.StreamChatAsync(messages, slmOptions, ct))
            {
                yield return token;
            }
            yield break;
        }

        // Complete failure
        yield return "⚠️ LLM 서버에 연결할 수 없습니다. 잠시 후 다시 시도해 주세요.";
    }

    public async Task<string> CompleteChatAsync(
        IReadOnlyList<LlmChatMessage> messages,
        LlmOptions? options = null,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await foreach (var token in StreamChatAsync(messages, options, ct))
        {
            sb.Append(token);
        }
        return sb.ToString();
    }

    public Task<float[]> GetEmbeddingAsync(string text, bool isQuery = false, CancellationToken ct = default)
        => _embeddingClient.GetEmbeddingAsync(text, isQuery, ct);

    private async IAsyncEnumerable<string> StreamFromFallbackAsync(
        FallbackServerOptions opts,
        IReadOnlyList<LlmChatMessage> messages,
        LlmOptions? options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (opts.Provider.Equals("Tgi", StringComparison.OrdinalIgnoreCase))
        {
            await foreach (var token in StreamTgiAsync(opts, messages, options, ct))
                yield return token;
        }
        else
        {
            await foreach (var token in StreamOllamaAsync(opts, messages, options, ct))
                yield return token;
        }
    }

    private async IAsyncEnumerable<string> StreamOllamaAsync(
        FallbackServerOptions opts,
        IReadOnlyList<LlmChatMessage> messages,
        LlmOptions? options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(opts.BaseUrl),
            Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds)
        };
        var ollama = new OllamaApiClient(httpClient);
        var model = options?.Model ?? opts.ChatModel;
        ollama.SelectedModel = model;

        var chatRequest = new ChatRequest
        {
            Model = model,
            Messages = messages.Select(m => new Message
            {
                Role = m.Role.ToLowerInvariant() switch
                {
                    "system" => ChatRole.System,
                    "assistant" => ChatRole.Assistant,
                    _ => ChatRole.User
                },
                Content = m.Content
            }).ToList(),
            Stream = true,
            Options = OllamaLlmClient.BuildRequestOptions(options, opts.MaxTokens)
        };

        await foreach (var response in ollama.ChatAsync(chatRequest, ct))
        {
            if (response?.Message?.Content is { } content)
                yield return content;
        }
    }

    private static async IAsyncEnumerable<string> StreamTgiAsync(
        FallbackServerOptions opts,
        IReadOnlyList<LlmChatMessage> messages,
        LlmOptions? options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(opts.BaseUrl),
            Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds)
        };

        var payload = new
        {
            model = options?.Model ?? opts.ChatModel,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            stream = true,
            temperature = options?.Temperature ?? 0.1f,
            max_tokens = options?.MaxTokens ?? opts.MaxTokens
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = System.Net.Http.Json.JsonContent.Create(payload)
        };

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") yield break;

            string? content = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                content = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("delta")
                    .GetProperty("content")
                    .GetString();
            }
            catch
            {
                // Skip malformed SSE chunks
            }

            if (!string.IsNullOrEmpty(content))
                yield return content;
        }
    }
}
