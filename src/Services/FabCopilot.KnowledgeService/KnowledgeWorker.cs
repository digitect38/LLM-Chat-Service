using System.Text.Json;
using FabCopilot.Contracts.Constants;
using FabCopilot.Contracts.Enums;
using FabCopilot.Contracts.Messages;
using FabCopilot.Contracts.Models;
using FabCopilot.KnowledgeService.Services;
using FabCopilot.Llm.Interfaces;
using FabCopilot.Llm.Models;
using FabCopilot.Messaging.Interfaces;
using FabCopilot.Redis.Interfaces;

namespace FabCopilot.KnowledgeService;

public sealed class KnowledgeWorker : BackgroundService
{
    private const string QueueGroup = "knowledge-workers";

    private readonly IMessageBus _messageBus;
    private readonly IConversationStore _conversationStore;
    private readonly ILlmClient _llmClient;
    private readonly KnowledgeManager _knowledgeManager;
    private readonly ILogger<KnowledgeWorker> _logger;

    public KnowledgeWorker(
        IMessageBus messageBus,
        IConversationStore conversationStore,
        ILlmClient llmClient,
        KnowledgeManager knowledgeManager,
        ILogger<KnowledgeWorker> logger)
    {
        _messageBus = messageBus;
        _conversationStore = conversationStore;
        _llmClient = llmClient;
        _knowledgeManager = knowledgeManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "KnowledgeWorker started. Subscribing to {Subject} with queue group {QueueGroup}",
            NatsSubjects.KnowledgeExtractRequest, QueueGroup);

        await foreach (var envelope in _messageBus.SubscribeAsync<KnowledgeExtractRequest>(
                           NatsSubjects.KnowledgeExtractRequest, QueueGroup, stoppingToken))
        {
            var request = envelope.Payload;
            if (request is null)
            {
                _logger.LogWarning("Received envelope with null payload, skipping. TraceId={TraceId}", envelope.TraceId);
                continue;
            }

            _logger.LogInformation(
                "Processing knowledge extract request. ConversationId={ConversationId}, EquipmentId={EquipmentId}, TraceId={TraceId}",
                request.ConversationId, request.EquipmentId, envelope.TraceId);

            _ = ProcessExtractionAsync(request, envelope.TraceId, stoppingToken);
        }

        _logger.LogInformation("KnowledgeWorker stopped");
    }

    private async Task ProcessExtractionAsync(KnowledgeExtractRequest request, string traceId, CancellationToken ct)
    {
        try
        {
            // 1. Load conversation from Redis
            var conversation = await _conversationStore.GetAsync(request.ConversationId, ct);
            if (conversation is null || conversation.Messages.Count == 0)
            {
                _logger.LogWarning(
                    "No conversation found for extraction. ConversationId={ConversationId}",
                    request.ConversationId);
                return;
            }

            // 2. Build the extraction prompt
            var messages = BuildExtractionPrompt(conversation);

            // 3. Call the LLM to extract structured knowledge candidates
            var llmResponse = await _llmClient.CompleteChatAsync(messages, new LlmOptions
            {
                Temperature = 0.1f,
                MaxTokens = 2048
            }, ct);

            // 4. Parse the LLM response into knowledge candidates
            var candidates = ParseCandidates(llmResponse, request.EquipmentId);

            // 5. Store each candidate as a draft in the knowledge manager
            foreach (var candidate in candidates)
            {
                await _knowledgeManager.CreateDraftAsync(
                    candidate.Type,
                    candidate.Equipment,
                    candidate.Symptom,
                    candidate.RootCause,
                    candidate.Solution,
                    ct);
            }

            // 6. Publish the extraction result
            var result = new KnowledgeExtractResult
            {
                ConversationId = request.ConversationId,
                Candidates = candidates
            };

            await _messageBus.PublishAsync(
                NatsSubjects.KnowledgeExtractResult,
                MessageEnvelope<KnowledgeExtractResult>.Create(
                    "knowledge.extract.result", result, request.EquipmentId),
                ct);

            _logger.LogInformation(
                "Knowledge extraction completed. ConversationId={ConversationId}, CandidateCount={Count}",
                request.ConversationId, candidates.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Knowledge extraction cancelled. ConversationId={ConversationId}", request.ConversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error extracting knowledge. ConversationId={ConversationId}, TraceId={TraceId}",
                request.ConversationId, traceId);
        }
    }

    private static List<LlmChatMessage> BuildExtractionPrompt(Conversation conversation)
    {
        var messages = new List<LlmChatMessage>();

        var systemPrompt =
            """
            You are a knowledge extraction assistant for semiconductor fab equipment.
            Analyze the conversation and extract structured knowledge objects.
            Return a JSON array of objects, each with these fields:
            - type: string (e.g., "troubleshooting", "procedure", "alarm_resolution", "maintenance")
            - symptom: string (the problem observed)
            - rootCause: string (the identified root cause)
            - solution: string (the resolution or recommended action)
            - confidence: number (0.0 to 1.0)

            Only extract knowledge that represents genuine troubleshooting insights or procedures.
            Respond ONLY with a JSON array, no additional text.
            """;

        messages.Add(LlmChatMessage.System(systemPrompt));

        // Include the conversation history as context
        var conversationText = string.Join("\n",
            conversation.Messages.Select(m => $"[{m.Role}]: {m.Text}"));
        messages.Add(LlmChatMessage.User($"Extract knowledge from this conversation:\n\n{conversationText}"));

        return messages;
    }

    private static List<KnowledgeObject> ParseCandidates(string llmResponse, string equipmentId)
    {
        var candidates = new List<KnowledgeObject>();

        try
        {
            // Try to parse the JSON array from the LLM response
            var jsonStart = llmResponse.IndexOf('[');
            var jsonEnd = llmResponse.LastIndexOf(']');

            if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
            {
                return candidates;
            }

            var jsonArray = llmResponse[jsonStart..(jsonEnd + 1)];
            using var doc = JsonDocument.Parse(jsonArray);

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var candidate = new KnowledgeObject
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = element.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "unknown" : "unknown",
                    Equipment = equipmentId,
                    Symptom = element.TryGetProperty("symptom", out var symptomProp) ? symptomProp.GetString() : null,
                    RootCause = element.TryGetProperty("rootCause", out var rcProp) ? rcProp.GetString() : null,
                    Solution = element.TryGetProperty("solution", out var solProp) ? solProp.GetString() : null,
                    Confidence = element.TryGetProperty("confidence", out var confProp) ? confProp.GetDouble() : 0.5,
                    Status = KnowledgeStatus.PendingReview,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                candidates.Add(candidate);
            }
        }
        catch (JsonException)
        {
            // If parsing fails, return empty list; already logged at caller level
        }

        return candidates;
    }
}
