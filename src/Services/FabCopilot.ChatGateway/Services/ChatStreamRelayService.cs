using FabCopilot.Contracts.Messages;
using FabCopilot.Messaging.Interfaces;
using FabCopilot.Redis.Interfaces;
using Serilog.Context;

namespace FabCopilot.ChatGateway.Services;

public sealed class ChatStreamRelayService : BackgroundService
{
    private readonly IMessageBus _messageBus;
    private readonly IConnectionManager _connectionManager;
    private readonly IConversationStore _conversationStore;
    private readonly ILogger<ChatStreamRelayService> _logger;

    /// <summary>
    /// Wildcard subject that matches all chat stream subjects (chat.stream.*).
    /// NATS uses '>' for multi-level wildcard matching.
    /// </summary>
    private const string ChatStreamWildcard = "chat.stream.>";

    public ChatStreamRelayService(
        IMessageBus messageBus,
        IConnectionManager connectionManager,
        IConversationStore conversationStore,
        ILogger<ChatStreamRelayService> logger)
    {
        _messageBus = messageBus;
        _connectionManager = connectionManager;
        _conversationStore = conversationStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ChatStreamRelayService starting, subscribing to {Subject}", ChatStreamWildcard);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var envelope in _messageBus.SubscribeAsync<ChatStreamChunk>(
                    ChatStreamWildcard, queueGroup: "gateway-relay", ct: stoppingToken))
                {
                    if (envelope.Payload is null)
                    {
                        _logger.LogWarning("Received envelope with null payload on {Subject}", ChatStreamWildcard);
                        continue;
                    }

                    var chunk = envelope.Payload;
                    var conversationId = chunk.ConversationId;
                    var equipmentId = envelope.EquipmentId;

                    using (LogContext.PushProperty("CorrelationId", envelope.CorrelationId))
                    {
                        if (string.IsNullOrEmpty(equipmentId))
                        {
                            _logger.LogWarning(
                                "Received chunk for conversation {ConversationId} with no equipmentId, cannot route",
                                conversationId);
                            continue;
                        }

                        _logger.LogDebug(
                            "Relaying chunk for conversation {ConversationId} to equipment {EquipmentId} (complete={IsComplete})",
                            conversationId, equipmentId, chunk.IsComplete);

                        try
                        {
                            await _connectionManager.SendToEquipmentAsync(equipmentId, conversationId, chunk);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "Failed to relay chunk for conversation {ConversationId} to equipment {EquipmentId}",
                                conversationId, equipmentId);
                        }

                        // When the stream is complete, persist the assembled assistant response
                        if (chunk.IsComplete && !string.IsNullOrEmpty(chunk.Token))
                        {
                            try
                            {
                                var assistantMessage = new FabCopilot.Contracts.Models.ChatMessage
                                {
                                    Role = FabCopilot.Contracts.Enums.MessageRole.Assistant,
                                    Text = chunk.Token,
                                    Timestamp = DateTimeOffset.UtcNow
                                };

                                await _conversationStore.AppendMessageAsync(conversationId, assistantMessage, stoppingToken);

                                _logger.LogInformation(
                                    "Persisted assistant response for conversation {ConversationId}",
                                    conversationId);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex,
                                    "Failed to persist assistant response for conversation {ConversationId}",
                                    conversationId);
                            }
                        }
                    }
                }

                // Subscription completed (NATS idle timeout) — re-subscribe
                _logger.LogWarning("NATS subscription completed, re-subscribing to {Subject}", ChatStreamWildcard);
                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("ChatStreamRelayService stopping gracefully");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChatStreamRelayService encountered an error, re-subscribing in 2s");
                await Task.Delay(2000, stoppingToken);
            }
        }
    }
}
