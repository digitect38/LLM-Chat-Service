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
    private readonly RagPipelineMode _ragPipelineMode;
    private readonly ILogger<LlmWorker> _logger;

    public LlmWorker(
        IMessageBus messageBus,
        IConversationStore conversationStore,
        ILlmClient llmClient,
        IConfiguration configuration,
        ILogger<LlmWorker> logger)
    {
        _messageBus = messageBus;
        _conversationStore = conversationStore;
        _llmClient = llmClient;
        _logger = logger;

        var modeStr = configuration.GetValue<string>("Rag:PipelineMode");
        _ragPipelineMode = Enum.TryParse<RagPipelineMode>(modeStr, ignoreCase: true, out var mode)
            ? mode
            : RagPipelineMode.Advanced;
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

            // 2.5. Retrieve RAG context from vector store
            var ragResults = await RetrieveRagContextAsync(
                request.ConversationId, request.UserMessage, request.EquipmentId, ct);

            // 3. Build the LLM prompt messages (with RAG context)
            var llmMessages = BuildPromptMessages(request, conversation, ragResults);

            // 4. Stream the response from the LLM
            var fullResponse = new StringBuilder();

            var llmOptions = !string.IsNullOrEmpty(request.ModelId)
                ? new LlmOptions { Model = request.ModelId }
                : null;

            await foreach (var token in _llmClient.StreamChatAsync(llmMessages, llmOptions, ct))
            {
                // Filter out special tokens that Qwen2.5 sometimes leaks
                var filtered = SanitizeToken(token);
                if (string.IsNullOrEmpty(filtered))
                    continue;

                fullResponse.Append(filtered);

                var chunk = new ChatStreamChunk
                {
                    ConversationId = request.ConversationId,
                    Token = filtered,
                    IsComplete = false
                };

                await _messageBus.PublishAsync(
                    streamSubject,
                    MessageEnvelope<ChatStreamChunk>.Create("chat.stream.chunk", chunk, request.EquipmentId),
                    ct);
            }

            // 5. Append source citations if RAG results were used and LLM didn't already include them
            var responseText = fullResponse.ToString();
            var alreadyHasCitations = responseText.Contains("참고 문서") || responseText.Contains("참고문서");
            var sourceSuffix = alreadyHasCitations ? null : BuildSourceCitations(ragResults);
            if (!string.IsNullOrEmpty(sourceSuffix))
            {
                fullResponse.Append(sourceSuffix);

                var sourceChunk = new ChatStreamChunk
                {
                    ConversationId = request.ConversationId,
                    Token = sourceSuffix,
                    IsComplete = false
                };

                await _messageBus.PublishAsync(
                    streamSubject,
                    MessageEnvelope<ChatStreamChunk>.Create("chat.stream.chunk", sourceChunk, request.EquipmentId),
                    ct);
            }

            // 6. Publish final chunk with IsComplete = true
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

            // 7. Append the assistant message to the Redis conversation
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

    private static List<LlmChatMessage> BuildPromptMessages(
        ChatRequest request, Conversation conversation, List<RetrievalResult> ragResults)
    {
        var messages = new List<LlmChatMessage>();

        // System message with equipment context and RAG documents
        var systemPrompt = BuildSystemPrompt(request.EquipmentId, request.Context, ragResults);
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

    internal static string BuildSystemPrompt(
        string equipmentId, EquipmentContext? context, List<RetrievalResult> ragResults)
    {
        var prompt = $"""
            You are an equipment copilot assistant for semiconductor fab equipment.
            You help engineers diagnose issues, find procedures, and troubleshoot problems.
            Equipment: {equipmentId}.

            [CRITICAL LANGUAGE RULE - YOU MUST FOLLOW THIS]
            - You MUST respond ONLY in Korean (한국어).
            - NEVER use Chinese characters (中文/漢字/한문). This is strictly forbidden.
            - If you catch yourself writing any Chinese character, stop and rewrite in Korean.
            - Technical terms (e.g. CMP, slurry, polishing pad) may remain in English.
            - Example of WRONG output: "이 문제는 設備의 故障으로..." (contains 漢字)
            - Example of CORRECT output: "이 문제는 장비의 고장으로..." (pure Korean)

            [FORMATTING RULES]
            - Use Markdown for structured responses (headings, lists, bold, etc.).
            - When expressing mathematical formulas, use LaTeX notation.
            - Inline math: $formula$ (e.g. $v = r \times \omega$)
            - Block math: $$formula$$ (e.g. $$MRR = K_p \times P \times V$$)
            """;

        if (context is not null)
        {
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
        }

        if (ragResults is { Count: > 0 })
        {
            prompt += """

            [REFERENCE DOCUMENTS - MANDATORY USE]
            1. ALWAYS use the following reference documents as your PRIMARY source of information.
            2. Base your answer on the documents FIRST, before using general knowledge.
            3. Cite the source document name in your answer: "📄 [파일명]에 따르면..." (e.g. "📄 cmp-troubleshooting.md에 따르면...")
            4. If NONE of the documents are relevant to the question, explicitly state: "참고 문서에 관련 정보가 없어 일반 지식을 바탕으로 답변합니다."
            5. NEVER contradict information in the reference documents.
            6. At the end of your answer, list all referenced sources under "---\n📚 **참고 문서:**" section.

            """;

            for (var i = 0; i < ragResults.Count; i++)
            {
                var result = ragResults[i];
                var sourceName = ExtractSourceName(result);
                prompt += $"""
                --- Document {i + 1}: {sourceName} (score: {result.Score:F3}) ---
                {result.ChunkText}

                """;
            }
        }

        return prompt;
    }

    private async Task<List<RetrievalResult>> RetrieveRagContextAsync(
        string conversationId, string userMessage, string equipmentId, CancellationToken ct)
    {
        var responseSubject = NatsSubjects.RagResponse(conversationId);

        using var ragCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ragCts.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            // 1. Get the enumerator to trigger NATS subscription registration
            var enumerator = _messageBus.SubscribeAsync<RagResponse>(
                    responseSubject, queueGroup: null, ragCts.Token)
                .GetAsyncEnumerator(ragCts.Token);

            // Start waiting for the first message (triggers SubscribeCoreAsync internally)
            var moveNextTask = enumerator.MoveNextAsync().AsTask();

            // Brief yield to ensure subscription is registered on NATS server
            await Task.Delay(50, ragCts.Token);

            // 2. Publish the RAG request
            var ragRequest = new RagRequest
            {
                Query = userMessage,
                EquipmentId = equipmentId,
                TopK = 3,
                ConversationId = conversationId,
                PipelineMode = _ragPipelineMode
            };

            await _messageBus.PublishAsync(
                NatsSubjects.RagRequest,
                MessageEnvelope<RagRequest>.Create("rag.request", ragRequest, equipmentId),
                ragCts.Token);

            _logger.LogDebug("Published RAG request. ConversationId={ConversationId}", conversationId);

            // 3. Wait for the response
            if (await moveNextTask)
            {
                var envelope = enumerator.Current;
                if (envelope.Payload is not null)
                {
                    _logger.LogInformation(
                        "Received RAG response. ConversationId={ConversationId}, ResultCount={Count}",
                        conversationId, envelope.Payload.Results.Count);

                    await enumerator.DisposeAsync();
                    return envelope.Payload.Results;
                }
            }

            await enumerator.DisposeAsync();
            return [];
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "RAG request timed out or cancelled. Proceeding without RAG. ConversationId={ConversationId}",
                conversationId);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RAG request failed. Proceeding without RAG. ConversationId={ConversationId}",
                conversationId);
            return [];
        }
    }

    internal static string BuildSourceCitations(List<RetrievalResult> ragResults)
    {
        if (ragResults is not { Count: > 0 })
            return string.Empty;

        var sources = ragResults
            .Select(r => ExtractSourceName(r))
            .Where(name => name != "unknown")
            .Distinct()
            .ToList();

        if (sources.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("📚 **참고 문서:**");
        foreach (var source in sources)
        {
            sb.AppendLine($"- 📄 {source} (score: {ragResults.First(r => ExtractSourceName(r) == source).Score:F3})");
        }

        return sb.ToString();
    }

    private static string ExtractSourceName(RetrievalResult result)
    {
        // Metadata values may be JsonElement after NATS serialization, so use ToString()
        if (TryGetMetadataString(result.Metadata, "file_name", out var fileName))
            return fileName;

        if (TryGetMetadataString(result.Metadata, "file_path", out var filePath))
            return filePath;

        if (TryGetMetadataString(result.Metadata, "document_id", out var docId))
            return docId;

        return !string.IsNullOrEmpty(result.DocumentId) ? result.DocumentId : "unknown";
    }

    private static bool TryGetMetadataString(Dictionary<string, object> metadata, string key, out string value)
    {
        value = string.Empty;
        if (!metadata.TryGetValue(key, out var raw) || raw is null)
            return false;

        var str = raw.ToString()?.Trim('"') ?? string.Empty;
        if (string.IsNullOrEmpty(str))
            return false;

        value = str;
        return true;
    }

    private static string SanitizeToken(string token)
    {
        if (string.IsNullOrEmpty(token))
            return token;

        // Strip Qwen2.5 special tokens that leak into output
        return token
            .Replace("<|im_start|>", "")
            .Replace("<|im_end|>", "")
            .Replace("<|endoftext|>", "");
    }
}
