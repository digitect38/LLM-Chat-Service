using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FabCopilot.Contracts.Messages;

namespace FabCopilot.WebClient.Services;

public sealed class ChatService : IAsyncDisposable
{
    private readonly string _gatewayBaseUrl;
    private readonly ILogger<ChatService> _logger;
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

    public async Task SendMessageAsync(string conversationId, string equipmentId, string message, string? modelId = null)
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

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}
