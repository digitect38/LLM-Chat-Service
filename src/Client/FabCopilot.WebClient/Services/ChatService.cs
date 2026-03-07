using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FabCopilot.Contracts.Messages;

namespace FabCopilot.WebClient.Services;

public sealed class ChatService : IAsyncDisposable
{
    private readonly string _gatewayBaseUrl;
    private readonly string _gatewayHttpUrl;
    private readonly ILogger<ChatService> _logger;
    private readonly HttpClient _httpClient = new();
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;

    public event Action<ChatStreamChunk>? OnMessageReceived;
    public event Action<WebSocketState>? OnStateChanged;

    public WebSocketState State => _webSocket?.State ?? WebSocketState.None;
    public bool IsConnected => _webSocket?.State == WebSocketState.Open;

    public ChatService(IConfiguration configuration, ILogger<ChatService> logger)
    {
        _gatewayBaseUrl = configuration["Gateway:WebSocketUrl"]
                          ?? "ws://localhost:5000/ws/chat";
        _gatewayHttpUrl = configuration["Gateway:HttpUrl"]
                          ?? "http://localhost:5000";
        _logger = logger;
    }

    public async Task ConnectAsync(string equipmentId, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync();

        _webSocket = new ClientWebSocket();
        _receiveCts = new CancellationTokenSource();

        var uri = new Uri($"{_gatewayBaseUrl.TrimEnd('/')}/{equipmentId}");
        _logger.LogInformation("Connecting to WebSocket at {Uri}", uri);

        try
        {
            await _webSocket.ConnectAsync(uri, cancellationToken);
            _logger.LogInformation("WebSocket connected to {Uri}", uri);
            OnStateChanged?.Invoke(_webSocket.State);

            _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), _receiveCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to WebSocket at {Uri}", uri);
            OnStateChanged?.Invoke(_webSocket.State);
            throw;
        }
    }

    public async Task SendMessageAsync(string conversationId, string equipmentId, string message, string? modelId = null, string searchMode = "hybrid", string? logContext = null)
    {
        if (_webSocket is null || _webSocket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket is not connected.");
        }

        var request = new ChatRequest
        {
            ConversationId = conversationId,
            EquipmentId = equipmentId,
            UserMessage = message,
            ModelId = modelId,
            SearchMode = searchMode,
            LogContext = logContext,
            Context = null
        };

        var json = JsonSerializer.Serialize(request);
        var bytes = Encoding.UTF8.GetBytes(json);

        _logger.LogDebug("Sending message: {Json}", json);

        await _webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            CancellationToken.None);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        try
        {
            while (!cancellationToken.IsCancellationRequested
                   && _webSocket is not null
                   && _webSocket.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await _webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("WebSocket received close frame");
                        OnStateChanged?.Invoke(WebSocketState.Closed);
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);

                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(ms.ToArray());
                    _logger.LogDebug("Received: {Json}", json);

                    var chunk = JsonSerializer.Deserialize<ChatStreamChunk>(json);
                    if (chunk is not null)
                    {
                        OnMessageReceived?.Invoke(chunk);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Receive loop cancelled");
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            _logger.LogWarning("WebSocket connection closed prematurely");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in WebSocket receive loop");
        }
        finally
        {
            OnStateChanged?.Invoke(_webSocket?.State ?? WebSocketState.Closed);
        }
    }

    public async Task DisconnectAsync()
    {
        if (_receiveCts is not null)
        {
            await _receiveCts.CancelAsync();
            _receiveCts.Dispose();
            _receiveCts = null;
        }

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask;
            }
            catch (OperationCanceledException) { }
            _receiveTask = null;
        }

        if (_webSocket is not null)
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                try
                {
                    await _webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Client disconnecting",
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error closing WebSocket");
                }
            }

            _webSocket.Dispose();
            _webSocket = null;
        }
    }

    public async Task SendFeedbackAsync(string conversationId, string equipmentId, int messageIndex, bool isPositive)
    {
        try
        {
            var feedback = new FeedbackMessage
            {
                ConversationId = conversationId,
                EquipmentId = equipmentId,
                MessageIndex = messageIndex,
                IsPositive = isPositive,
                Timestamp = DateTimeOffset.UtcNow
            };

            var json = JsonSerializer.Serialize(feedback);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_gatewayHttpUrl.TrimEnd('/')}/api/feedback", content);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send feedback for conversation {ConversationId}", conversationId);
        }
    }

    public async Task<List<ConversationSummary>> GetConversationListAsync(string equipmentId)
    {
        try
        {
            var url = $"{_gatewayHttpUrl.TrimEnd('/')}/api/conversations/{Uri.EscapeDataString(equipmentId)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<ConversationSummary>>(json) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load conversation list for equipment {EquipmentId}", equipmentId);
            return [];
        }
    }

    public async Task<bool> DeleteConversationAsync(string equipmentId, string conversationId)
    {
        try
        {
            var url = $"{_gatewayHttpUrl.TrimEnd('/')}/api/conversations/{Uri.EscapeDataString(equipmentId)}/{Uri.EscapeDataString(conversationId)}";
            var response = await _httpClient.DeleteAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete conversation {ConversationId}", conversationId);
            return false;
        }
    }

    public async Task<ConversationDetail?> GetConversationAsync(string equipmentId, string conversationId)
    {
        try
        {
            var url = $"{_gatewayHttpUrl.TrimEnd('/')}/api/conversations/{Uri.EscapeDataString(equipmentId)}/{Uri.EscapeDataString(conversationId)}";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<ConversationDetail>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load conversation {ConversationId}", conversationId);
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        await DisconnectAsync();
    }
}

public sealed class ConversationSummary
{
    [System.Text.Json.Serialization.JsonPropertyName("conversationId")]
    public string ConversationId { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("lastUpdated")]
    public DateTimeOffset LastUpdated { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("messageCount")]
    public int MessageCount { get; set; }
}

public sealed class ConversationDetail
{
    [System.Text.Json.Serialization.JsonPropertyName("conversationId")]
    public string ConversationId { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("equipmentId")]
    public string EquipmentId { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("messages")]
    public List<ConversationDetailMessage> Messages { get; set; } = [];
}

public sealed class ConversationDetailMessage
{
    [System.Text.Json.Serialization.JsonPropertyName("role")]
    public int Role { get; set; } // 0=User, 1=Assistant

    [System.Text.Json.Serialization.JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }
}
