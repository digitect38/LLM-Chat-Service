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
using Serilog.Context;

namespace FabCopilot.ChatGateway.Services;

public sealed class ConnectionManager : IConnectionManager
{
    // equipmentId -> (connectionId -> WebSocket): multiple clients per equipment
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, WebSocket>> _equipmentConnections = new();

    // conversationId -> connectionId: route responses to the connection that initiated the request
    private readonly ConcurrentDictionary<string, string> _conversationToConnection = new();

    private readonly IMessageBus _messageBus;
    private readonly IConversationStore _conversationStore;
    private readonly IAuditTrail _auditTrail;
    private readonly ILogger<ConnectionManager> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ConnectionManager(
        IMessageBus messageBus,
        IConversationStore conversationStore,
        IAuditTrail auditTrail,
        ILogger<ConnectionManager> logger)
    {
        _messageBus = messageBus;
        _conversationStore = conversationStore;
        _auditTrail = auditTrail;
        _logger = logger;
    }

    public int ActiveConnectionCount => _equipmentConnections.Values.Sum(d => d.Count);

    public async Task HandleConnectionAsync(string equipmentId, WebSocket ws, CancellationToken ct)
    {
        var connectionId = Guid.NewGuid().ToString("N");

        // Register the connection under this equipment
        var connectionsForEquipment = _equipmentConnections.GetOrAdd(equipmentId, _ => new ConcurrentDictionary<string, WebSocket>());
        connectionsForEquipment[connectionId] = ws;

        _logger.LogInformation(
            "WebSocket connected for equipment {EquipmentId}, connectionId={ConnectionId}. " +
            "Equipment connections: {EquipCount}, Total: {TotalCount}",
            equipmentId, connectionId, connectionsForEquipment.Count, ActiveConnectionCount);

        var buffer = new byte[4096];

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("WebSocket close requested by equipment {EquipmentId}, connectionId={ConnectionId}",
                        equipmentId, connectionId);
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

                    await ProcessIncomingMessageAsync(equipmentId, connectionId, json, ct);
                }
            }
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "WebSocket error for equipment {EquipmentId}, connectionId={ConnectionId}",
                equipmentId, connectionId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("WebSocket connection cancelled for equipment {EquipmentId}, connectionId={ConnectionId}",
                equipmentId, connectionId);
        }
        finally
        {
            RemoveConnection(equipmentId, connectionId);

            _logger.LogInformation(
                "WebSocket disconnected for equipment {EquipmentId}, connectionId={ConnectionId}. Total: {TotalCount}",
                equipmentId, connectionId, ActiveConnectionCount);

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
        if (!_equipmentConnections.TryGetValue(equipmentId, out var connectionsForEquipment) || connectionsForEquipment.IsEmpty)
        {
            _logger.LogWarning(
                "No active WebSocket connections for equipment {EquipmentId}, dropping chunk for conversation {ConversationId}",
                equipmentId, conversationId);
            return;
        }

        // Targeted routing: conversationId -> connectionId -> specific WebSocket
        if (!string.IsNullOrEmpty(conversationId) &&
            _conversationToConnection.TryGetValue(conversationId, out var targetConnectionId) &&
            connectionsForEquipment.TryGetValue(targetConnectionId, out var targetWs))
        {
            if (!await SendToSocketAsync(equipmentId, targetConnectionId, targetWs, chunk))
            {
                RemoveConnection(equipmentId, targetConnectionId);
            }
            return;
        }

        // Fallback: broadcast to all connections for this equipment
        _logger.LogDebug(
            "No targeted connection for conversation {ConversationId} on equipment {EquipmentId}, broadcasting to {Count} connections",
            conversationId, equipmentId, connectionsForEquipment.Count);

        var deadConnections = new List<string>();

        foreach (var (connId, ws) in connectionsForEquipment)
        {
            if (!await SendToSocketAsync(equipmentId, connId, ws, chunk))
            {
                deadConnections.Add(connId);
            }
        }

        foreach (var deadConnId in deadConnections)
        {
            RemoveConnection(equipmentId, deadConnId);
        }
    }

    private async Task<bool> SendToSocketAsync(string equipmentId, string connectionId, WebSocket ws, ChatStreamChunk chunk)
    {
        if (ws.State != WebSocketState.Open)
        {
            _logger.LogWarning(
                "WebSocket not open for equipment {EquipmentId}, connectionId={ConnectionId}, removing stale connection",
                equipmentId, connectionId);
            return false;
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
            return true;
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex,
                "Failed to send chunk to equipment {EquipmentId}, connectionId={ConnectionId}",
                equipmentId, connectionId);
            return false;
        }
    }

    private void RemoveConnection(string equipmentId, string connectionId)
    {
        // Remove from equipment connections
        if (_equipmentConnections.TryGetValue(equipmentId, out var connectionsForEquipment))
        {
            connectionsForEquipment.TryRemove(connectionId, out _);

            if (connectionsForEquipment.IsEmpty)
            {
                _equipmentConnections.TryRemove(equipmentId, out _);
            }
        }

        // Remove conversation mappings that point to this connectionId
        var staleConversations = _conversationToConnection
            .Where(kvp => kvp.Value == connectionId)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var convId in staleConversations)
        {
            _conversationToConnection.TryRemove(convId, out _);
        }
    }

    private async Task ProcessIncomingMessageAsync(string equipmentId, string connectionId, string json, CancellationToken ct)
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

        // Map this conversation to the connection that initiated it
        _conversationToConnection[chatRequest.ConversationId] = connectionId;

        _logger.LogInformation(
            "Received chat request from equipment {EquipmentId}, conversation {ConversationId}, connectionId={ConnectionId}: {MessagePreview}",
            equipmentId,
            chatRequest.ConversationId,
            connectionId,
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
        var correlationId = Guid.NewGuid().ToString("N");
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            var envelope = MessageEnvelope<ChatRequest>.Create(
                type: "chat.request",
                payload: chatRequest,
                equipmentId: equipmentId,
                correlationId: correlationId);

            await _messageBus.PublishAsync(NatsSubjects.ChatRequest, envelope, ct);

            // Log query to audit trail (fire-and-forget)
            _ = _auditTrail.LogQueryAsync(equipmentId, chatRequest.ConversationId, chatRequest.UserMessage, ct);

            _logger.LogInformation(
                "Published chat request for conversation {ConversationId} to {Subject}",
                chatRequest.ConversationId,
                NatsSubjects.ChatRequest);
        }
    }
}
