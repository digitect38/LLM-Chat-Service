using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using FabCopilot.Contracts.Constants;
using FabCopilot.Contracts.Enums;
using FabCopilot.Contracts.Messages;
using FabCopilot.Contracts.Models;
using FabCopilot.Llm.Interfaces;
using FabCopilot.Llm.Models;
using FabCopilot.Messaging.Interfaces;
using FabCopilot.Observability.Metrics;
using FabCopilot.Redis.Interfaces;

namespace FabCopilot.LlmService;

public sealed class LlmWorker : BackgroundService
{
    private const string QueueGroup = "llm-workers";

    private readonly IMessageBus _messageBus;
    private readonly IConversationStore _conversationStore;
    private readonly IAuditTrail _auditTrail;
    private readonly ILlmClient _llmClient;
    private readonly RagPipelineMode _ragPipelineMode;
    private readonly int _ragTopK;
    private readonly float _gateAThreshold;
    private readonly bool _gateBEnabled;
    private readonly bool _gateCEnabled;
    private readonly ILogger<LlmWorker> _logger;

    public LlmWorker(
        IMessageBus messageBus,
        IConversationStore conversationStore,
        IAuditTrail auditTrail,
        ILlmClient llmClient,
        IConfiguration configuration,
        ILogger<LlmWorker> logger)
    {
        _messageBus = messageBus;
        _conversationStore = conversationStore;
        _auditTrail = auditTrail;
        _llmClient = llmClient;
        _logger = logger;

        var modeStr = configuration.GetValue<string>("Rag:PipelineMode");
        _ragPipelineMode = Enum.TryParse<RagPipelineMode>(modeStr, ignoreCase: true, out var mode)
            ? mode
            : RagPipelineMode.Advanced;

        _ragTopK = configuration.GetValue<int?>("Rag:DefaultTopK") ?? 5;
        _gateAThreshold = configuration.GetValue<float?>("ResponsePolicy:GateAThreshold") ?? 0.55f;
        _gateBEnabled = configuration.GetValue<bool?>("ResponsePolicy:GateBEnabled") ?? true;
        _gateCEnabled = configuration.GetValue<bool?>("ResponsePolicy:GateCEnabled") ?? true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LlmWorker started. Subscribing to {Subject} with queue group {QueueGroup}",
            NatsSubjects.ChatRequest, QueueGroup);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
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

                // Subscription completed (NATS idle timeout) — re-subscribe
                _logger.LogWarning("NATS subscription completed, re-subscribing to {Subject}", NatsSubjects.ChatRequest);
                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
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
            var ragSw = FabMetrics.StartTimer();
            var ragResponse = await RetrieveRagContextAsync(
                request.ConversationId, request.UserMessage, request.EquipmentId, ct);
            FabMetrics.RecordElapsed(ragSw, FabMetrics.LlmRagRetrievalDuration);
            var ragResults = ragResponse?.Results ?? [];

            // Gate A: Reject if RAG max score is too low (no confident context)
            // Strict mode raises the threshold significantly
            var effectiveThreshold = ComputeEffectiveThreshold(request.SearchMode, _gateAThreshold);
            var maxScore = ragResults.Count > 0 ? ragResults.Max(r => r.Score) : 0f;
            var isConfident = EvaluateConfidence(ragResults, maxScore, effectiveThreshold);

            if (ragResults.Count > 0 && !isConfident)
            {
                FabMetrics.LlmGateATriggeredCount.Add(1);
                _logger.LogInformation(
                    "Gate A triggered: MaxScore={MaxScore} < Threshold={Threshold}. ConversationId={ConversationId}",
                    maxScore, _gateAThreshold, request.ConversationId);
            }

            // 3. Build the LLM prompt messages (with RAG context, Gate A awareness, intent routing)
            var queryIntent = ragResponse?.QueryIntent;
            var llmMessages = BuildPromptMessages(request, conversation, ragResults, isConfident, queryIntent);

            // 4. Stream the response from the LLM
            FabMetrics.LlmRequestCount.Add(1);
            var llmTotalSw = FabMetrics.StartTimer();
            var firstTokenSw = FabMetrics.StartTimer();
            var firstTokenRecorded = false;
            var fullResponse = new StringBuilder();

            var llmOptions = !string.IsNullOrEmpty(request.ModelId)
                ? new LlmOptions { Model = request.ModelId }
                : null;

            // State for filtering DeepSeek R1 <think>...</think> blocks
            var insideThinkBlock = false;
            var tagBuffer = new StringBuilder();

            await foreach (var token in _llmClient.StreamChatAsync(llmMessages, llmOptions, ct))
            {
                // Record first token latency
                if (!firstTokenRecorded)
                {
                    FabMetrics.RecordElapsed(firstTokenSw, FabMetrics.LlmFirstTokenDuration);
                    firstTokenRecorded = true;
                }

                // Filter out special tokens that Qwen2.5 sometimes leaks
                var sanitized = SanitizeToken(token);
                if (string.IsNullOrEmpty(sanitized))
                    continue;

                // Filter out DeepSeek R1 <think>...</think> reasoning blocks
                var filtered = FilterThinkBlocks(sanitized, ref insideThinkBlock, tagBuffer);
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

            FabMetrics.RecordElapsed(llmTotalSw, FabMetrics.LlmTotalDuration);

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

            // Ghost Citation Prevention: filter out RAG results not reflected in response
            var filteredRagResults = FilterGhostCitations(responseText, ragResults);

            // Gate B: Add disclaimer if citations are missing or partially reflected
            if (_gateBEnabled && ragResults.Count > 0)
            {
                var finalText = fullResponse.ToString();
                var hasCitation = finalText.Contains("📄") || finalText.Contains("참고 문서") || finalText.Contains("참고문서");

                if (!hasCitation || filteredRagResults.Count == 0)
                {
                    var disclaimer = "\n\n> ⚠️ *이 답변은 문서 기반으로 확인되지 않았습니다. 정확한 정보는 관련 문서를 직접 확인하시기 바랍니다.*";
                    fullResponse.Append(disclaimer);

                    var disclaimerChunk = new ChatStreamChunk
                    {
                        ConversationId = request.ConversationId,
                        Token = disclaimer,
                        IsComplete = false
                    };

                    await _messageBus.PublishAsync(
                        streamSubject,
                        MessageEnvelope<ChatStreamChunk>.Create("chat.stream.chunk", disclaimerChunk, request.EquipmentId),
                        ct);

                    FabMetrics.LlmGateBTriggeredCount.Add(1);
                    _logger.LogInformation(
                        "Gate B triggered: No citations found in response. ConversationId={ConversationId}",
                        request.ConversationId);
                }
                else if (filteredRagResults.Count < ragResults.Count * 0.5)
                {
                    var partialWarning = "\n\n> ⚠️ *일부 참고 문서만 답변에 반영되었습니다.*";
                    fullResponse.Append(partialWarning);

                    var warningChunk = new ChatStreamChunk
                    {
                        ConversationId = request.ConversationId,
                        Token = partialWarning,
                        IsComplete = false
                    };

                    await _messageBus.PublishAsync(
                        streamSubject,
                        MessageEnvelope<ChatStreamChunk>.Create("chat.stream.chunk", warningChunk, request.EquipmentId),
                        ct);

                    FabMetrics.LlmGateBTriggeredCount.Add(1);
                    _logger.LogInformation(
                        "Gate B triggered: Partial citations ({FilteredCount}/{TotalCount}). ConversationId={ConversationId}",
                        filteredRagResults.Count, ragResults.Count, request.ConversationId);
                }
            }

            // Gate C: Response quality validation
            if (_gateCEnabled)
            {
                var gateCResult = EvaluateGateC(responseText, ragResults);
                if (!gateCResult.Passed)
                {
                    var warningText = "\n\n> ⚠️ **응답 품질 경고:**\n" +
                                      string.Join("\n", gateCResult.Warnings.Select(w => $"> - {w}"));
                    fullResponse.Append(warningText);

                    var gateCChunk = new ChatStreamChunk
                    {
                        ConversationId = request.ConversationId,
                        Token = warningText,
                        IsComplete = false
                    };

                    await _messageBus.PublishAsync(
                        streamSubject,
                        MessageEnvelope<ChatStreamChunk>.Create("chat.stream.chunk", gateCChunk, request.EquipmentId),
                        ct);

                    FabMetrics.LlmGateCTriggeredCount.Add(1);
                    _logger.LogInformation(
                        "Gate C triggered: {WarningCount} warnings. ConversationId={ConversationId}",
                        gateCResult.Warnings.Count, request.ConversationId);
                }
            }

            // 6. Publish final chunk with IsComplete = true and citations (using filtered results)
            var citations = filteredRagResults
                .Where(r => r.Score > 0)
                .Select((r, idx) =>
                {
                    var sourceName = ExtractSourceName(r);
                    var isPdf = sourceName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
                    var resolvedDocId = TryGetMetadataString(r.Metadata, "document_id", out var docId) ? docId : r.DocumentId;
                    var chapter = TryGetMetadataString(r.Metadata, "chapter", out var ch) ? ch : null;
                    var section = TryGetMetadataString(r.Metadata, "section", out var sec) ? sec : null;
                    var lineStart = TryGetMetadataInt(r.Metadata, "line_start");
                    var lineEnd = TryGetMetadataInt(r.Metadata, "line_end");

                    var lineRange = lineStart.HasValue && lineEnd.HasValue
                        ? new LineRangeInfo { From = lineStart.Value, To = lineEnd.Value }
                        : null;

                    var displayRef = BuildDisplayRef(resolvedDocId, chapter, section, lineRange,
                        TryGetMetadataInt(r.Metadata, "page_number"));

                    return new CitationInfo
                    {
                        CitationId = $"cite-{idx + 1}",
                        DocId = resolvedDocId,
                        FileName = sourceName,
                        ChunkId = r.DocumentId,
                        ChunkText = r.ChunkText,
                        Chapter = chapter,
                        Section = section,
                        Page = TryGetMetadataInt(r.Metadata, "page_number"),
                        CharOffsetStart = TryGetMetadataInt(r.Metadata, "char_offset_start"),
                        CharOffsetEnd = TryGetMetadataInt(r.Metadata, "char_offset_end"),
                        PdfUrl = isPdf ? $"/api/documents/{sourceName}" : null,
                        ParentContext = TryGetMetadataString(r.Metadata, "parent_context", out var pc) ? pc : null,
                        Score = r.Score,
                        HighlightType = TryGetMetadataString(r.Metadata, "highlight_type", out var ht) ? ht : "text",
                        Revision = TryGetMetadataString(r.Metadata, "revision", out var rev) ? rev : null,
                        LineRange = lineRange,
                        DisplayRef = displayRef
                    };
                })
                .ToList();

            var completeChunk = new ChatStreamChunk
            {
                ConversationId = request.ConversationId,
                Token = string.Empty,
                IsComplete = true,
                Citations = citations.Count > 0 ? citations : null
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

            // 8. Log response to audit trail (fire-and-forget)
            _ = _auditTrail.LogResponseAsync(
                request.EquipmentId, request.ConversationId,
                fullResponse.Length, llmTotalSw.Elapsed.TotalMilliseconds, ct);

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
        ChatRequest request, Conversation conversation, List<RetrievalResult> ragResults,
        bool isConfident = true, QueryIntent? intent = null)
    {
        var messages = new List<LlmChatMessage>();

        // System message with equipment context, RAG documents, and intent routing
        var systemPrompt = BuildSystemPrompt(request.EquipmentId, request.Context, ragResults, isConfident, intent);
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

    // Citation format constants (avoid interpolation issues in raw strings)
    private const string CitationFormatExample = "[DOC_ID-Chapter-Section-{Line:FromLine-ToLine}]";
    private const string CitationFormatSample = "[MNL-2025-001-Ch3-S3.2.1-{Line:142-158}]";
    private const string CitationFormatFallback = "[DOC_ID-Ch-S-{Page:XX}]";
    private const string CitationPrecisionPrompt = "[DOC_ID-Chapter-Section-{Line:FromLine-ToLine}]";

    internal static float ComputeEffectiveThreshold(string? searchMode, float baseThreshold)
    {
        return string.Equals(searchMode, "strict", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(baseThreshold, 0.75f)
            : baseThreshold;
    }

    internal static bool EvaluateConfidence(List<RetrievalResult> ragResults, float maxScore, float effectiveThreshold)
    {
        return ragResults.Count == 0 || maxScore >= effectiveThreshold;
    }

    internal static string BuildSystemPrompt(
        string equipmentId, EquipmentContext? context, List<RetrievalResult> ragResults,
        bool isConfident = true, QueryIntent? intent = null)
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

            [RESPONSE FORMAT]
            Structure your responses using these sections when applicable:
            ## 요약
            → 1-2줄 핵심 요약

            ## 상세
            → 단계별 상세 설명, 번호 매기기 사용

            ## 참조
            → 인용한 문서명과 해당 섹션 (📄 파일명)

            ## 신뢰도
            → 참고 문서 기반 확신 수준 (높음/중간/낮음)

            Note: 간단한 질문에는 '요약'과 '참조'만 사용해도 됩니다.

            [CITATION FORMAT - MANDATORY]
            - 모든 인용에 정밀 출처를 반드시 포함하세요.
            - 형식: {CitationFormatExample}
            - 예: {CitationFormatSample}
            - DOC_ID: 문서 식별자, Chapter: 장(Ch), Section: 절, Line: 참조 행 범위
            - 각 문서의 DOC_ID와 Chapter/Section 정보는 아래 참조 문서에 명시되어 있습니다.
            - 문서에 행 번호가 명시되지 않은 경우 페이지 번호를 사용: {CitationFormatFallback}
            - 모든 답변에서 근거가 되는 출처를 반드시 명시하세요.
            """;

        // Intent-based response style routing
        prompt += BuildIntentStyleSection(intent);

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

        if (ragResults is not { Count: > 0 })
        {
            prompt += """

            [NO REFERENCE DOCUMENTS AVAILABLE]
            참고 문서를 찾지 못했습니다. 답변 시 "참고 문서에 관련 정보가 없어 일반 지식을 바탕으로 답변합니다."라고 반드시 명시하세요.
            추측이나 불확실한 정보는 포함하지 마세요.
            """;
        }
        else if (!isConfident)
        {
            prompt += """

            [LOW CONFIDENCE - GATE A WARNING]
            검색된 참고 문서의 관련성 점수가 매우 낮습니다.
            이 질문에 대해 신뢰할 수 있는 문서 기반 답변을 제공하기 어렵습니다.
            답변 시작 부분에 반드시 다음 경고를 포함하세요:
            "⚠️ 관련 문서의 신뢰도가 낮아 정확하지 않을 수 있습니다. 반드시 원본 문서를 확인하세요."

            참고 문서는 아래와 같지만 관련성이 낮을 수 있습니다:

            """;

            for (var i = 0; i < ragResults.Count; i++)
            {
                var result = ragResults[i];
                var sourceName = ExtractSourceName(result);
                prompt += $"""
                --- Document {i + 1}: {sourceName} (score: {result.Score:F3}, LOW CONFIDENCE) ---
                {result.ChunkText}

                """;
            }
        }
        else
        {
            prompt += """

            [REFERENCE DOCUMENTS - MANDATORY USE]
            1. ALWAYS use the following reference documents as your PRIMARY source of information.
            2. Base your answer on the documents FIRST, before using general knowledge.
            3. Cite using precision format: {CitationPrecisionPrompt} (각 문서 메타데이터 참조)
            4. If NONE of the documents are relevant to the question, explicitly state: "참고 문서에 관련 정보가 없어 일반 지식을 바탕으로 답변합니다."
            5. NEVER contradict information in the reference documents.
            6. At the end of your answer, list all referenced sources under "---\n📚 **참고 문서:**" section with precision citation format.

            """;

            for (var i = 0; i < ragResults.Count; i++)
            {
                var result = ragResults[i];
                var sourceName = ExtractSourceName(result);
                var docChapter = TryGetMetadataString(result.Metadata, "chapter", out var rch) ? rch : "-";
                var docSection = TryGetMetadataString(result.Metadata, "section", out var rsc) ? rsc : "-";
                var docLineStart = TryGetMetadataInt(result.Metadata, "line_start");
                var docLineEnd = TryGetMetadataInt(result.Metadata, "line_end");
                var lineInfo = docLineStart.HasValue && docLineEnd.HasValue
                    ? $"lines: {docLineStart}-{docLineEnd}"
                    : $"page: {TryGetMetadataInt(result.Metadata, "page_number")?.ToString() ?? "-"}";
                prompt += $"""
                --- Document {i + 1}: {sourceName} (score: {result.Score:F3}, chapter: {docChapter}, section: {docSection}, {lineInfo}) ---
                {result.ChunkText}

                """;
            }
        }

        return prompt;
    }

    private async Task<RagResponse?> RetrieveRagContextAsync(
        string conversationId, string userMessage, string equipmentId, CancellationToken ct)
    {
        var responseSubject = NatsSubjects.RagResponse(conversationId);

        using var ragCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ragCts.CancelAfter(TimeSpan.FromSeconds(60));

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
                TopK = _ragTopK,
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
                        "Received RAG response. ConversationId={ConversationId}, ResultCount={Count}, MaxScore={MaxScore}, Intent={Intent}",
                        conversationId, envelope.Payload.Results.Count, envelope.Payload.MaxScore, envelope.Payload.QueryIntent);

                    await enumerator.DisposeAsync();
                    return envelope.Payload;
                }
            }

            await enumerator.DisposeAsync();
            return null;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "RAG request timed out (60s) or cancelled. Proceeding WITHOUT RAG context — answers will lack document references. ConversationId={ConversationId}",
                conversationId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RAG request failed. Proceeding without RAG. ConversationId={ConversationId}",
                conversationId);
            return null;
        }
    }

    internal static string BuildSourceCitations(List<RetrievalResult> ragResults)
    {
        if (ragResults is not { Count: > 0 })
            return string.Empty;

        var cited = new HashSet<string>();
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("📚 **참고 문서:**");

        foreach (var r in ragResults)
        {
            var sourceName = ExtractSourceName(r);
            if (sourceName == "unknown" || !cited.Add(sourceName))
                continue;

            var docId = TryGetMetadataString(r.Metadata, "document_id", out var did) ? did : sourceName;
            var chapter = TryGetMetadataString(r.Metadata, "chapter", out var ch) ? ch : null;
            var section = TryGetMetadataString(r.Metadata, "section", out var sec) ? sec : null;
            var lineStart = TryGetMetadataInt(r.Metadata, "line_start");
            var lineEnd = TryGetMetadataInt(r.Metadata, "line_end");

            var lineRange = lineStart.HasValue && lineEnd.HasValue
                ? new LineRangeInfo { From = lineStart.Value, To = lineEnd.Value }
                : null;

            var displayRef = BuildDisplayRef(docId, chapter, section, lineRange,
                TryGetMetadataInt(r.Metadata, "page_number"));

            sb.AppendLine($"- 📄 [{displayRef}] (score: {r.Score:F3})");
        }

        return cited.Count == 0 ? string.Empty : sb.ToString();
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

    private static int? TryGetMetadataInt(Dictionary<string, object> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var raw) || raw is null)
            return null;

        var str = raw.ToString()?.Trim('"') ?? string.Empty;
        return int.TryParse(str, out var val) ? val : null;
    }

    /// <summary>
    /// Filters out DeepSeek R1 &lt;think&gt;...&lt;/think&gt; reasoning blocks from streamed tokens.
    /// Handles tags that may span across multiple token boundaries.
    /// </summary>
    internal static string FilterThinkBlocks(string token, ref bool insideThinkBlock, StringBuilder tagBuffer)
    {
        var result = new StringBuilder();

        for (var i = 0; i < token.Length; i++)
        {
            var ch = token[i];

            if (tagBuffer.Length > 0)
            {
                // We're accumulating a potential tag
                tagBuffer.Append(ch);
                var buf = tagBuffer.ToString();

                if (buf.Equals("<think>", StringComparison.OrdinalIgnoreCase))
                {
                    insideThinkBlock = true;
                    tagBuffer.Clear();
                }
                else if (buf.Equals("</think>", StringComparison.OrdinalIgnoreCase))
                {
                    insideThinkBlock = false;
                    tagBuffer.Clear();
                }
                else if (!"<think>".StartsWith(buf, StringComparison.OrdinalIgnoreCase) &&
                         !"</think>".StartsWith(buf, StringComparison.OrdinalIgnoreCase))
                {
                    // Not a think tag — flush buffer
                    if (!insideThinkBlock)
                        result.Append(buf);
                    tagBuffer.Clear();
                }
                // else: still a valid prefix, keep accumulating
            }
            else if (ch == '<')
            {
                tagBuffer.Append(ch);
            }
            else if (!insideThinkBlock)
            {
                result.Append(ch);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Builds a v3.3 precision display reference string.
    /// Format: "DOC_ID-Chapter-Section-{Line:From-To}" or fallback "DOC_ID-{Page:XX}".
    /// </summary>
    internal static string BuildDisplayRef(string docId, string? chapter, string? section,
        LineRangeInfo? lineRange, int? page)
    {
        var parts = new List<string> { docId };

        if (!string.IsNullOrEmpty(chapter))
            parts.Add(chapter);

        if (!string.IsNullOrEmpty(section))
            parts.Add($"S{section}");

        if (lineRange is not null)
            parts.Add($"{{Line:{lineRange.From}-{lineRange.To}}}");
        else if (page.HasValue)
            parts.Add($"{{Page:{page.Value}}}");

        return string.Join("-", parts);
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

    // ─── Gate C: Response Quality Validation ────────────────────────

    internal record GateCResult(bool Passed, List<string> Warnings);

    internal static GateCResult EvaluateGateC(string responseText, List<RetrievalResult> ragResults)
    {
        var warnings = new List<string>();
        var text = responseText?.Trim() ?? string.Empty;

        // 1. Empty response check
        if (string.IsNullOrWhiteSpace(text) || text.All(c => !char.IsLetterOrDigit(c)))
        {
            warnings.Add("응답이 비어 있거나 유효한 내용이 없습니다.");
            return new GateCResult(false, warnings);
        }

        // 2. Minimum length check (< 50 characters)
        if (text.Length < 50)
        {
            warnings.Add("응답이 너무 짧습니다 (50자 미만).");
        }

        // 3. Repetition detection: same sentence/word repeated 3+ times
        if (HasExcessiveRepetition(text))
        {
            warnings.Add("응답에 반복적인 내용이 감지되었습니다.");
        }

        // 4. Chinese character (漢字) check — violates system prompt language rule
        if (ContainsChineseCharacters(text))
        {
            warnings.Add("응답에 한자(漢字)가 포함되어 있습니다. 한국어만 사용해야 합니다.");
        }

        // 5. Structure check: RAG docs present but no 참조 section
        if (ragResults is { Count: > 0 } &&
            !text.Contains("참조") && !text.Contains("참고 문서") && !text.Contains("참고문서") && !text.Contains("📄"))
        {
            warnings.Add("참고 문서가 제공되었으나 응답에 참조 섹션이 없습니다.");
        }

        return new GateCResult(warnings.Count == 0, warnings);
    }

    internal static bool HasExcessiveRepetition(string text)
    {
        // Check for repeated words (3+ consecutive identical words)
        var words = Regex.Split(text, @"\s+").Where(w => w.Length >= 2).ToList();
        for (var i = 0; i < words.Count - 2; i++)
        {
            if (string.Equals(words[i], words[i + 1], StringComparison.OrdinalIgnoreCase) &&
                string.Equals(words[i], words[i + 2], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Check for repeated sentences (split by period, 3+ identical sentences)
        var sentences = text.Split(new[] { '.', '。' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length >= 5)
            .ToList();

        var sentenceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var sentence in sentences)
        {
            sentenceCounts.TryGetValue(sentence, out var count);
            sentenceCounts[sentence] = count + 1;
            if (count + 1 >= 3) return true;
        }

        return false;
    }

    internal static bool ContainsChineseCharacters(string text)
    {
        foreach (var ch in text)
        {
            // CJK Unified Ideographs (main block used by Chinese characters)
            if (ch is >= '\u4E00' and <= '\u9FFF')
                return true;
            // CJK Compatibility Ideographs
            if (ch is >= '\uF900' and <= '\uFAFF')
                return true;
        }
        return false;
    }

    // ─── Ghost Citation Prevention ──────────────────────────────────

    internal static List<RetrievalResult> FilterGhostCitations(string responseText, List<RetrievalResult> ragResults)
    {
        if (ragResults is not { Count: > 0 })
            return [];

        if (string.IsNullOrWhiteSpace(responseText))
            return [];

        var responseLower = responseText.ToLowerInvariant();
        var filtered = new List<RetrievalResult>();

        foreach (var result in ragResults)
        {
            var keywords = ExtractKeywords(result.ChunkText);
            if (keywords.Count == 0)
            {
                filtered.Add(result);
                continue;
            }

            var matchCount = keywords.Count(kw => responseLower.Contains(kw));
            var matchRatio = (double)matchCount / keywords.Count;

            // Keep if 3+ keywords matched OR 30%+ keywords matched
            if (matchCount >= 3 || matchRatio >= 0.3)
            {
                filtered.Add(result);
            }
        }

        return filtered;
    }

    internal static List<string> ExtractKeywords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        // Extract words of 2+ characters, lowercase, deduplicate
        var words = Regex.Split(text.ToLowerInvariant(), @"[\s\p{P}]+")
            .Where(w => w.Length >= 2)
            .Where(w => !IsStopWord(w))
            .Distinct()
            .ToList();

        return words;
    }

    private static bool IsStopWord(string word)
    {
        // Common Korean/English stop words to exclude from keyword matching
        return word is "the" or "and" or "for" or "that" or "this" or "with" or "from" or "are" or "was" or "has"
            or "있는" or "하는" or "되는" or "이는" or "또는" or "등의" or "위한" or "대한" or "통해" or "경우";
    }

    // ─── Intent-based Response Style Routing ────────────────────────

    internal static string BuildIntentStyleSection(QueryIntent? intent)
    {
        if (intent is null or QueryIntent.General)
            return string.Empty;

        var style = intent switch
        {
            QueryIntent.Error => """

            [RESPONSE STYLE - ERROR/ALARM]
            이 질문은 알람/에러 관련입니다. 다음 구조로 답변하세요:
            1. **알람 코드**: 해당 알람 코드 명시
            2. **원인**: 발생 가능한 원인 목록
            3. **조치 방법**: 단계별 해결 절차
            4. **예방 조치**: 재발 방지를 위한 권장 사항
            """,
            QueryIntent.Procedure => """

            [RESPONSE STYLE - PROCEDURE]
            이 질문은 절차/방법 관련입니다. 다음 구조로 답변하세요:
            1. **사전 준비**: 필요한 도구, 안전 장비, 전제 조건
            2. **절차**: 번호 매기기를 사용한 단계별 절차 (Step 1, Step 2, ...)
            3. **주의사항**: 각 단계의 주의점 또는 팁
            4. **완료 확인**: 절차 완료 후 확인 사항
            """,
            QueryIntent.Part => """

            [RESPONSE STYLE - PART/CONSUMABLE]
            이 질문은 부품/소모품 관련입니다. 다음 구조로 답변하세요:
            | 항목 | 내용 |
            |------|------|
            | 부품명 | ... |
            | 규격 | ... |
            | 수명/교체주기 | ... |
            | 호환 부품 | ... |
            표 형식으로 부품 정보를 정리하세요.
            """,
            QueryIntent.Definition => """

            [RESPONSE STYLE - DEFINITION]
            이 질문은 정의/개념 관련입니다. 다음 구조로 답변하세요:
            1. **정의**: 간결하고 명확한 1-2줄 정의
            2. **관련 용어**: 연관된 기술 용어와 간략한 설명
            3. **적용 예시**: 실제 장비에서의 적용 사례 (해당 시)
            """,
            QueryIntent.Spec => """

            [RESPONSE STYLE - SPECIFICATION]
            이 질문은 사양/파라미터 관련입니다. 다음 구조로 답변하세요:
            | 파라미터 | 값/범위 | 단위 | 비고 |
            |----------|---------|------|------|
            표 형식으로 사양 정보를 정리하세요. 수치와 범위를 정확히 명시하세요.
            """,
            QueryIntent.Comparison => """

            [RESPONSE STYLE - COMPARISON]
            이 질문은 비교 관련입니다. 다음 구조로 답변하세요:
            | 비교 항목 | A | B |
            |-----------|---|---|
            표 형식으로 비교하고, 각 항목의 장단점을 명시하세요.
            """,
            _ => string.Empty
        };

        return style;
    }
}
