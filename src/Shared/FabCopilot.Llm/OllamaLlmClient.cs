using System.Runtime.CompilerServices;
using System.Text;
using FabCopilot.Llm.Configuration;
using FabCopilot.Llm.Interfaces;
using FabCopilot.Llm.Models;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;

namespace FabCopilot.Llm;

public sealed class OllamaLlmClient : ILlmClient
{
    private readonly OllamaApiClient _ollama;
    private readonly OllamaOptions _options;

    public OllamaLlmClient(IOptions<OllamaOptions> options)
    {
        _options = options.Value;
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(_options.BaseUrl),
            Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds)
        };
        _ollama = new OllamaApiClient(httpClient);
        _ollama.SelectedModel = _options.ChatModel;
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<LlmChatMessage> messages,
        LlmOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = options?.Model ?? _options.ChatModel;

        var chatRequest = new ChatRequest
        {
            Model = model,
            Messages = ConvertMessages(messages),
            Stream = true,
            Options = BuildRequestOptions(options)
        };

        await foreach (var response in _ollama.ChatAsync(chatRequest, ct))
        {
            if (response?.Message?.Content is { } content)
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

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var response = await _ollama.EmbedAsync(
            new EmbedRequest
            {
                Model = _options.EmbeddingModel,
                Input = [text]
            },
            ct);

        if (response?.Embeddings is { Count: > 0 })
        {
            var embedding = response.Embeddings[0];
            return embedding.Select(d => (float)d).ToArray();
        }

        return [];
    }

    internal static RequestOptions BuildRequestOptions(LlmOptions? options) => new()
    {
        Temperature = options?.Temperature ?? 0.1f,
        NumPredict = options?.MaxTokens ?? 2048
    };

    private static List<Message> ConvertMessages(IReadOnlyList<LlmChatMessage> messages)
    {
        return messages.Select(m => new Message
        {
            Role = MapRole(m.Role),
            Content = m.Content
        }).ToList();
    }

    private static ChatRole MapRole(string role) => role.ToLowerInvariant() switch
    {
        "system" => ChatRole.System,
        "assistant" => ChatRole.Assistant,
        "user" => ChatRole.User,
        _ => ChatRole.User
    };
}
