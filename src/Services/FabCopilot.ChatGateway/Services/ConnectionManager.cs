using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FabCopilot.Contracts.Constants;
using FabCopilot.Contracts.Enums;
using FabCopilot.Contracts.Messages;
using FabCopilot.Contracts.Models;
using FabCopilot.Messaging.Interfaces;
using FabCopilot.Redis.Interfaces;

namespace FabCopilot.ChatGateway.Services;

public sealed class ConnectionManager : IConnectionManager
{
    private readonly ConcurrentDictionary<string, WebSocket> _connections = new();
    private readonly IMessageBus _messageBus;
    private readonly IConversationStore _conversationStore;
    private readonly ILogger<ConnectionManager> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ConnectionManager(
        IMessageBus messageBus,
        IConversationStore conversationStore,
        ILogger<ConnectionManager> logger)
    {
        _messageBus = messageBus;
        _conversationStore = conversationStore;
        _logger = logger;
    }

    public int ActiveConnectionCount => _connections.Count;

    public async Task HandleConnectionAsync(string equipmentId, WebSocket ws, CancellationToken ct)
    {
        // Register the connection (replaces any existing connection for this equipmentId)
        var previousSocket = _connections.AddOrUpdate(equipmentId, ws, (_, __) => ws);
        _logger.LogInformation("WebSocket connected for equipment {EquipmentId}. Active connections: {Count}",
            equipmentId, _connections.Count);

        var buffer = new byte[4096];

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("WebSocket close requested by equipment {EquipmentId}", equipmentId);
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", CancellationToken.None);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    // Handle fragmented messages by accumulating until EndOfMessage
                    if (!result.EndOfMessage)
                    {
                        var messageBuilder = new StringBuilder(json);
                        while (!result.EndOfMessage)
                        {
                            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                            messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        }
                        json = messageBuilder.ToString();
                    }

                    await ProcessIncomingMessageAsync(equipmentId, json, ct);
                }
            }
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "WebSocket error for equipment {EquipmentId}", equipmentId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("WebSocket connection cancelled for equipment {EquipmentId}", equipmentId);
        }
        finally
        {
            _connections.TryRemove(equipmentId, out _);
            _logger.LogInformation("WebSocket disconnected for equipment {EquipmentId}. Active connections: {Count}",
                equipmentId, _connections.Count);

            if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server closing", CancellationToken.None);
                }
                catch
                {
                    // Best-effort close; ignore errors during cleanup
                }
            }

            ws.Dispose();
        }
    }

    public async Task SendToEquipmentAsync(string equipmentId, string conversationId, ChatStreamChunk chunk)
    {
        if (!_connections.TryGetValue(equipmentId, out var ws))
        {
            _logger.LogWarning("No active WebSocket for equipment {EquipmentId}, dropping chunk for conversation {ConversationId}",
                equipmentId, conversationId);
            return;
        }

        if (ws.State != WebSocketState.Open)
        {
            _logger.LogWarning("WebSocket not open for equipment {EquipmentId}, removing stale connection", equipmentId);
            _connections.TryRemove(equipmentId, out _);
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(chunk, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                CancellationToken.None);
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "Failed to send chunk to equipment {EquipmentId}, removing connection", equipmentId);
            _connections.TryRemove(equipmentId, out _);
        }
    }

    private async Task ProcessIncomingMessageAsync(string equipmentId, string json, CancellationToken ct)
    {
        ChatRequest? chatRequest;
        try
        {
            chatRequest = JsonSerializer.Deserialize<ChatRequest>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize message from equipment {EquipmentId}", equipmentId);
            return;
        }

        if (chatRequest is null)
        {
            _logger.LogWarning("Received null chat request from equipment {EquipmentId}", equipmentId);
            return;
        }

        // Auto-set equipment ID from the WebSocket route
        chatRequest.EquipmentId = equipmentId;

        // Generate a conversation ID if not provided
        if (string.IsNullOrWhiteSpace(chatRequest.ConversationId))
        {
            chatRequest.ConversationId = Guid.NewGuid().ToString();
        }

        _logger.LogInformation(
            "Received chat request from equipment {EquipmentId}, conversation {ConversationId}: {MessagePreview}",
            equipmentId,
            chatRequest.ConversationId,
            chatRequest.UserMessage.Length > 80
                ? chatRequest.UserMessage[..80] + "..."
                : chatRequest.UserMessage);

        // Ensure conversation exists in Redis, create if new
        var existing = await _conversationStore.GetAsync(chatRequest.ConversationId, ct);
        if (existing is null)
        {
            var newConversation = new Conversation
            {
                ConversationId = chatRequest.ConversationId,
                EquipmentId = equipmentId,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdatedAt = DateTimeOffset.UtcNow
            };
            await _conversationStore.SaveAsync(newConversation, ct);
        }

        // Persist the user message to Redis
        var chatMessage = new ChatMessage
        {
            Role = MessageRole.User,
            Text = chatRequest.UserMessage,
            Timestamp = DateTimeOffset.UtcNow
        };

        await _conversationStore.AppendMessageAsync(chatRequest.ConversationId, chatMessage, ct);

        // Publish the chat request to the NATS message bus
        var envelope = MessageEnvelope<ChatRequest>.Create(
            type: "chat.request",
            payload: chatRequest,
            equipmentId: equipmentId);

        await _messageBus.PublishAsync(NatsSubjects.ChatRequest, envelope, ct);

        _logger.LogInformation(
            "Published chat request for conversation {ConversationId} to {Subject}",
            chatRequest.ConversationId,
            NatsSubjects.ChatRequest);
    }
}
