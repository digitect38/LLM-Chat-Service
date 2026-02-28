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
    private readonly IOptionsMonitor<OllamaOptions> _optionsMonitor;
    private readonly IEmbeddingClient _embeddingClient;

    public OllamaLlmClient(IOptionsMonitor<OllamaOptions> optionsMonitor, IEmbeddingClient embeddingClient)
    {
        _optionsMonitor = optionsMonitor;
        _embeddingClient = embeddingClient;
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<LlmChatMessage> messages,
        LlmOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var currentOptions = _optionsMonitor.CurrentValue;
        var model = options?.Model ?? currentOptions.ChatModel;

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(currentOptions.BaseUrl),
            Timeout = TimeSpan.FromSeconds(currentOptions.TimeoutSeconds)
        };
        var ollama = new OllamaApiClient(httpClient);
        ollama.SelectedModel = model;

        var chatRequest = new ChatRequest
        {
            Model = model,
            Messages = ConvertMessages(messages),
            Stream = true,
            Options = BuildRequestOptions(options, currentOptions.MaxTokens)
        };

        await foreach (var response in ollama.ChatAsync(chatRequest, ct))
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

    public Task<float[]> GetEmbeddingAsync(string text, bool isQuery = false, CancellationToken ct = default)
        => _embeddingClient.GetEmbeddingAsync(text, isQuery, ct);

    internal static RequestOptions BuildRequestOptions(LlmOptions? options, int defaultMaxTokens = 4096) => new()
    {
        Temperature = options?.Temperature ?? 0.1f,
        NumPredict = options?.MaxTokens ?? defaultMaxTokens
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
