using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FabCopilot.Llm.Configuration;
using FabCopilot.Llm.Interfaces;
using FabCopilot.Llm.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FabCopilot.Llm;

public sealed class TgiLlmClient : ILlmClient
{
    private readonly IOptionsMonitor<TgiOptions> _optionsMonitor;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly ILogger<TgiLlmClient> _logger;

    public TgiLlmClient(
        IOptionsMonitor<TgiOptions> optionsMonitor,
        IEmbeddingClient embeddingClient,
        ILogger<TgiLlmClient> logger)
    {
        _optionsMonitor = optionsMonitor;
        _embeddingClient = embeddingClient;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<LlmChatMessage> messages,
        LlmOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var tgiOptions = _optionsMonitor.CurrentValue;
        var model = options?.Model ?? tgiOptions.ChatModel;

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(tgiOptions.BaseUrl),
            Timeout = TimeSpan.FromSeconds(tgiOptions.TimeoutSeconds)
        };

        var payload = new
        {
            model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            stream = true,
            temperature = options?.Temperature ?? 0.1f,
            max_tokens = options?.MaxTokens ?? 2048
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(payload)
        };

        _logger.LogDebug("Requesting TGI chat completion from {BaseUrl} with model {Model}",
            tgiOptions.BaseUrl, model);

        using var response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;

            if (!line.StartsWith("data: ")) continue;

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
            catch (Exception ex)
            {
                _logger.LogTrace("Skipping SSE chunk: {Error}", ex.Message);
            }

            if (!string.IsNullOrEmpty(content))
            {
                yield return content;
            }
        }
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
}
