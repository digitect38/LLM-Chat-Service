using System.Text.RegularExpressions;
using FabCopilot.Contracts.Constants;
using FabCopilot.Contracts.Messages;
using FabCopilot.Llm.Interfaces;
using FabCopilot.Messaging.Interfaces;
using FabCopilot.RagService.Configuration;
using FabCopilot.VectorStore.Configuration;
using FabCopilot.VectorStore.Interfaces;
using FabCopilot.VectorStore.Models;
using Microsoft.Extensions.Options;

namespace FabCopilot.RagService;

public sealed class RagWorker : BackgroundService
{
    private const string QueueGroup = "rag-workers";

    private readonly IMessageBus _messageBus;
    private readonly ILlmClient _llmClient;
    private readonly IVectorStore _vectorStore;
    private readonly QdrantOptions _qdrantOptions;
    private readonly RagOptions _ragOptions;
    private readonly ILogger<RagWorker> _logger;

    public RagWorker(
        IMessageBus messageBus,
        ILlmClient llmClient,
        IVectorStore vectorStore,
        IOptions<QdrantOptions> qdrantOptions,
        IOptions<RagOptions> ragOptions,
        ILogger<RagWorker> logger)
    {
        _messageBus = messageBus;
        _llmClient = llmClient;
        _vectorStore = vectorStore;
        _qdrantOptions = qdrantOptions.Value;
        _ragOptions = ragOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "RagWorker started. Subscribing to {Subject} with queue group {QueueGroup}",
            NatsSubjects.RagRequest, QueueGroup);

        await foreach (var envelope in _messageBus.SubscribeAsync<RagRequest>(
                           NatsSubjects.RagRequest, QueueGroup, stoppingToken))
        {
            var request = envelope.Payload;
            if (request is null)
            {
                _logger.LogWarning(
                    "Received envelope with null payload, skipping. TraceId={TraceId}",
                    envelope.TraceId);
                continue;
            }

            _logger.LogInformation(
                "Processing RAG request. Query={Query}, EquipmentId={EquipmentId}, TopK={TopK}, TraceId={TraceId}",
                request.Query, request.EquipmentId, request.TopK, envelope.TraceId);

            _ = ProcessRagRequestAsync(request, stoppingToken);
        }

        _logger.LogInformation("RagWorker stopped");
    }

    private async Task ProcessRagRequestAsync(RagRequest request, CancellationToken ct)
    {
        var conversationId = request.ConversationId ?? Guid.NewGuid().ToString();
        var responseSubject = NatsSubjects.RagResponse(conversationId);

        try
        {
            // 1. Embed the query text
            _logger.LogDebug("Generating embedding for query. ConversationId={ConversationId}", conversationId);
            var queryVector = await _llmClient.GetEmbeddingAsync(request.Query, ct);

            // 2. Over-fetch candidates from vector store for hybrid re-ranking
            var collection = _qdrantOptions.DefaultCollection;
            var overFetchK = Math.Max(request.TopK * 10, 100);
            var searchResults = await _vectorStore.SearchAsync(
                collection, queryVector, overFetchK, filter: null, ct);

            _logger.LogInformation(
                "Vector search completed. ConversationId={ConversationId}, OverFetchK={OverFetchK}, ResultCount={ResultCount}",
                conversationId, overFetchK, searchResults.Count);

            // 2.5. Pre-filter by MinScore on the large candidate pool
            var candidates = FilterByScore(searchResults, _ragOptions.MinScore);

            _logger.LogInformation(
                "Pre-filter applied. ConversationId={ConversationId}, Before={Before}, After={After}, MinScore={MinScore}",
                conversationId, searchResults.Count, candidates.Count, _ragOptions.MinScore);

            // 3. Re-rank with expanded keyword boost and take desired TopK
            var keywords = ExtractKeywords(request.Query);
            var expanded = ExpandKeywords(keywords);
            var reranked = RerankWithKeywordBoost(candidates, expanded);
            var filtered = reranked.Take(request.TopK).ToList();

            _logger.LogInformation(
                "Keyword re-ranking applied. ConversationId={ConversationId}, Keywords=[{Keywords}], Expanded=[{Expanded}], TopK={TopK}, ResultCount={ResultCount}",
                conversationId, string.Join(", ", keywords), string.Join(", ", expanded), request.TopK, filtered.Count);

            // 4. Map results to response
            var retrievalResults = filtered.Select(r => new RetrievalResult
            {
                DocumentId = r.Id,
                ChunkText = r.Payload.TryGetValue("text", out var text) ? text.ToString() ?? string.Empty : string.Empty,
                Score = r.Score,
                Metadata = new Dictionary<string, object>(r.Payload)
            }).ToList();

            var response = new RagResponse
            {
                ConversationId = conversationId,
                Results = retrievalResults
            };

            // 5. Publish response
            await _messageBus.PublishAsync(
                responseSubject,
                MessageEnvelope<RagResponse>.Create("rag.response", response, request.EquipmentId),
                ct);

            _logger.LogInformation(
                "Published RAG response. ConversationId={ConversationId}, Subject={Subject}, ResultCount={ResultCount}",
                conversationId, responseSubject, retrievalResults.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation(
                "RAG request cancelled. ConversationId={ConversationId}", conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing RAG request. ConversationId={ConversationId}", conversationId);

            // Publish an empty response so callers are not left waiting
            try
            {
                var errorResponse = new RagResponse
                {
                    ConversationId = conversationId,
                    Results = []
                };

                await _messageBus.PublishAsync(
                    responseSubject,
                    MessageEnvelope<RagResponse>.Create("rag.response.error", errorResponse, request.EquipmentId),
                    ct);
            }
            catch (Exception publishEx)
            {
                _logger.LogError(publishEx,
                    "Failed to publish error response. ConversationId={ConversationId}", conversationId);
            }
        }
    }

    internal static List<VectorSearchResult> FilterByScore(
        IReadOnlyList<VectorSearchResult> results, float minScore)
        => results.Where(r => r.Score >= minScore).ToList();

    /// <summary>
    /// Extracts content keywords from a query by removing Korean particles and short words.
    /// </summary>
    internal static List<string> ExtractKeywords(string query)
    {
        // Remove common Korean particles, endings, and punctuation
        var cleaned = Regex.Replace(query,
            @"[은는이가을를의에서로부터까지와과도만요인가요습니까세요죠나요?!.,\s]+", " ");
        return cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Expands keywords with domain-specific related terms to improve recall.
    /// Timing/criteria intent words are expanded to match section headers and content
    /// that contain criteria, thresholds, and specifications.
    /// </summary>
    internal static readonly Dictionary<string, string[]> QueryExpansionMap = new()
    {
        ["시기"] = ["기준", "시간", "수명", "주기"],
        ["언제"] = ["기준", "시간", "시점"],
        ["원인"] = ["이유", "문제", "발생"],
        ["방법"] = ["절차", "순서", "단계"],
        ["주기"] = ["기준", "시간", "수명"],
    };

    internal static List<string> ExpandKeywords(List<string> keywords)
    {
        var expanded = new HashSet<string>(keywords);
        foreach (var kw in keywords)
        {
            if (QueryExpansionMap.TryGetValue(kw, out var related))
            {
                foreach (var r in related)
                    expanded.Add(r);
            }
        }
        return expanded.ToList();
    }

    /// <summary>
    /// Re-ranks vector search results by boosting scores for chunks that contain query keywords.
    /// Each keyword match adds a boost to the original vector similarity score.
    /// </summary>
    internal static List<VectorSearchResult> RerankWithKeywordBoost(
        IReadOnlyList<VectorSearchResult> results, List<string> keywords, float boostPerKeyword = 0.10f)
    {
        if (keywords.Count == 0)
            return results.OrderByDescending(r => r.Score).ToList();

        return results
            .Select(r =>
            {
                var text = r.Payload.TryGetValue("text", out var t) ? t.ToString() ?? "" : "";
                var matchCount = keywords.Count(kw => text.Contains(kw, StringComparison.OrdinalIgnoreCase));
                var boostedScore = r.Score + (matchCount * boostPerKeyword);
                return (Result: r, BoostedScore: boostedScore);
            })
            .OrderByDescending(x => x.BoostedScore)
            .Select(x => x.Result)
            .ToList();
    }
}
