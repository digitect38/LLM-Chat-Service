using System.Text;
using FabCopilot.Contracts.Constants;
using FabCopilot.Contracts.Enums;
using FabCopilot.Contracts.Messages;
using FabCopilot.Contracts.Models;
using FabCopilot.Llm.Interfaces;
using FabCopilot.Llm.Models;
using FabCopilot.Messaging.Interfaces;
using FabCopilot.Redis.Interfaces;

namespace FabCopilot.LlmService;

public sealed class LlmWorker : BackgroundService
{
    private const string QueueGroup = "llm-workers";

    private readonly IMessageBus _messageBus;
    private readonly IConversationStore _conversationStore;
    private readonly ILlmClient _llmClient;
    private readonly ILogger<LlmWorker> _logger;

    public LlmWorker(
        IMessageBus messageBus,
        IConversationStore conversationStore,
        ILlmClient llmClient,
        ILogger<LlmWorker> logger)
    {
        _messageBus = messageBus;
        _conversationStore = conversationStore;
        _llmClient = llmClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LlmWorker started. Subscribing to {Subject} with queue group {QueueGroup}",
            NatsSubjects.ChatRequest, QueueGroup);

        await foreach (var envelope in _messageBus.SubscribeAsync<ChatRequest>(
                           NatsSubjects.ChatRequest, QueueGroup, stoppingToken))
        {
            var request = envelope.Payload;
            if (request is null)
            {
                _logger.LogWarning("Received envelope with null payload, skipping. TraceId={TraceId}", envelope.TraceId);
                continue;
            }

            _logger.LogInformation(
                "Processing chat request. ConversationId={ConversationId}, EquipmentId={EquipmentId}, TraceId={TraceId}",
                request.ConversationId, request.EquipmentId, envelope.TraceId);

            _ = ProcessChatRequestAsync(request, stoppingToken);
        }

        _logger.LogInformation("LlmWorker stopped");
    }

    private async Task ProcessChatRequestAsync(ChatRequest request, CancellationToken ct)
    {
        var streamSubject = NatsSubjects.ChatStream(request.ConversationId);

        try
        {
            // 1. Get or create conversation from Redis
            var conversation = await _conversationStore.GetAsync(request.ConversationId, ct);
            if (conversation is null)
            {
                conversation = new Conversation
                {
                    ConversationId = request.ConversationId,
                    EquipmentId = request.EquipmentId
                };
                await _conversationStore.SaveAsync(conversation, ct);
                _logger.LogInformation("Created new conversation {ConversationId}", request.ConversationId);
            }

            // 2. Append the user message to the conversation
            var userMessage = new ChatMessage
            {
                Role = MessageRole.User,
                Text = request.UserMessage,
                Timestamp = DateTimeOffset.UtcNow
            };
            await _conversationStore.AppendMessageAsync(request.ConversationId, userMessage, ct);

            // 3. Build the LLM prompt messages
            var llmMessages = BuildPromptMessages(request, conversation);

            // 4. Stream the response from the LLM
            var fullResponse = new StringBuilder();

            await foreach (var token in _llmClient.StreamChatAsync(llmMessages, options: null, ct))
            {
                fullResponse.Append(token);

                var chunk = new ChatStreamChunk
                {
                    ConversationId = request.ConversationId,
                    Token = token,
                    IsComplete = false
                };

                await _messageBus.PublishAsync(
                    streamSubject,
                    MessageEnvelope<ChatStreamChunk>.Create("chat.stream.chunk", chunk, request.EquipmentId),
                    ct);
            }

            // 5. Publish final chunk with IsComplete = true
            var completeChunk = new ChatStreamChunk
            {
                ConversationId = request.ConversationId,
                Token = string.Empty,
                IsComplete = true
            };

            await _messageBus.PublishAsync(
                streamSubject,
                MessageEnvelope<ChatStreamChunk>.Create("chat.stream.complete", completeChunk, request.EquipmentId),
                ct);

            // 6. Append the assistant message to the Redis conversation
            var assistantMessage = new ChatMessage
            {
                Role = MessageRole.Assistant,
                Text = fullResponse.ToString(),
                Timestamp = DateTimeOffset.UtcNow
            };
            await _conversationStore.AppendMessageAsync(request.ConversationId, assistantMessage, ct);

            _logger.LogInformation(
                "Completed chat request. ConversationId={ConversationId}, ResponseLength={ResponseLength}",
                request.ConversationId, fullResponse.Length);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Chat request cancelled. ConversationId={ConversationId}", request.ConversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat request. ConversationId={ConversationId}", request.ConversationId);

            // Publish an error chunk so the client knows something went wrong
            try
            {
                var errorChunk = new ChatStreamChunk
                {
                    ConversationId = request.ConversationId,
                    Token = string.Empty,
                    IsComplete = true,
                    Error = ex.Message
                };

                await _messageBus.PublishAsync(
                    streamSubject,
                    MessageEnvelope<ChatStreamChunk>.Create("chat.stream.error", errorChunk, request.EquipmentId),
                    ct);
            }
            catch (Exception publishEx)
            {
                _logger.LogError(publishEx, "Failed to publish error chunk. ConversationId={ConversationId}", request.ConversationId);
            }
        }
    }

    private static List<LlmChatMessage> BuildPromptMessages(ChatRequest request, Conversation conversation)
    {
        var messages = new List<LlmChatMessage>();

        // System message with equipment context
        var systemPrompt = BuildSystemPrompt(request.EquipmentId, request.Context);
        messages.Add(LlmChatMessage.System(systemPrompt));

        // Conversation history
        foreach (var msg in conversation.Messages)
        {
            var llmMsg = msg.Role switch
            {
                MessageRole.User => LlmChatMessage.User(msg.Text),
                MessageRole.Assistant => LlmChatMessage.Assistant(msg.Text),
                MessageRole.System => LlmChatMessage.System(msg.Text),
                _ => null
            };

            if (llmMsg is not null)
            {
                messages.Add(llmMsg);
            }
        }

        // Current user question
        messages.Add(LlmChatMessage.User(request.UserMessage));

        return messages;
    }

    private static string BuildSystemPrompt(string equipmentId, EquipmentContext? context)
    {
        var prompt = $"You are an equipment copilot assistant for semiconductor fab equipment. " +
                     $"You help engineers diagnose issues, find procedures, and troubleshoot problems. " +
                     $"Equipment: {equipmentId}.";

        if (context is null)
        {
            return prompt;
        }

        var contextParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(context.Module))
            contextParts.Add($"Module: {context.Module}");

        if (!string.IsNullOrWhiteSpace(context.Recipe))
            contextParts.Add($"Recipe: {context.Recipe}");

        if (!string.IsNullOrWhiteSpace(context.ProcessState))
            contextParts.Add($"Process State: {context.ProcessState}");

        if (context.RecentAlarms is { Count: > 0 })
            contextParts.Add($"Recent Alarms: {string.Join(", ", context.RecentAlarms)}");

        if (contextParts.Count > 0)
        {
            prompt += $" Current context - {string.Join("; ", contextParts)}.";
        }

        return prompt;
    }
}
