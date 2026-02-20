using System.Text.RegularExpressions;
using FabCopilot.Contracts.Constants;
using FabCopilot.Contracts.Enums;
using FabCopilot.Contracts.Messages;
using FabCopilot.Llm.Interfaces;
using FabCopilot.Messaging.Interfaces;
using FabCopilot.RagService.Configuration;
using FabCopilot.RagService.Interfaces;
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
    private readonly IQueryRewriter? _queryRewriter;
    private readonly ILlmReranker? _llmReranker;
    private readonly IKnowledgeGraphStore? _graphStore;
    private readonly IAgenticRagOrchestrator? _agenticOrchestrator;
    private readonly ILogger<RagWorker> _logger;

    public RagWorker(
        IMessageBus messageBus,
        ILlmClient llmClient,
        IVectorStore vectorStore,
        IOptions<QdrantOptions> qdrantOptions,
        IOptions<RagOptions> ragOptions,
        ILogger<RagWorker> logger,
        IQueryRewriter? queryRewriter = null,
        ILlmReranker? llmReranker = null,
        IKnowledgeGraphStore? graphStore = null,
        IAgenticRagOrchestrator? agenticOrchestrator = null)
    {
        _messageBus = messageBus;
        _llmClient = llmClient;
        _vectorStore = vectorStore;
        _qdrantOptions = qdrantOptions.Value;
        _ragOptions = ragOptions.Value;
        _logger = logger;
        _queryRewriter = queryRewriter;
        _llmReranker = llmReranker;
        _graphStore = graphStore;
        _agenticOrchestrator = agenticOrchestrator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "RagWorker started. Subscribing to {Subject} with queue group {QueueGroup}. DefaultPipeline={DefaultPipeline}",
            NatsSubjects.RagRequest, QueueGroup, _ragOptions.DefaultPipelineMode);

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
                "Processing RAG request. Query={Query}, EquipmentId={EquipmentId}, TopK={TopK}, Pipeline={Pipeline}, TraceId={TraceId}",
                request.Query, request.EquipmentId, request.TopK, request.PipelineMode, envelope.TraceId);

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
            var pipelineMode = request.PipelineMode;

            var response = pipelineMode switch
            {
                RagPipelineMode.Advanced => await RunAdvancedPipelineAsync(request, conversationId, ct),
                RagPipelineMode.Graph => await RunGraphPipelineAsync(request, conversationId, ct),
                RagPipelineMode.Agentic => await RunAgenticPipelineAsync(request, conversationId, ct),
                _ => await RunNaivePipelineAsync(request, conversationId, ct)
            };

            // Publish response
            await _messageBus.PublishAsync(
                responseSubject,
                MessageEnvelope<RagResponse>.Create("rag.response", response, request.EquipmentId),
                ct);

            _logger.LogInformation(
                "Published RAG response. ConversationId={ConversationId}, Pipeline={Pipeline}, Subject={Subject}, ResultCount={ResultCount}",
                conversationId, pipelineMode, responseSubject, response.Results.Count);
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
                    Results = [],
                    PipelineMode = request.PipelineMode
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

    // ─── 1세대: Naive RAG ───────────────────────────────────────────────

    private async Task<RagResponse> RunNaivePipelineAsync(
        RagRequest request, string conversationId, CancellationToken ct)
    {
        _logger.LogDebug("Running Naive pipeline. ConversationId={ConversationId}", conversationId);

        // 1. Embed the query text
        var queryVector = await _llmClient.GetEmbeddingAsync(request.Query, ct);

        // 2. Over-fetch candidates from vector store
        var collection = _qdrantOptions.DefaultCollection;
        var overFetchK = Math.Max(request.TopK * 10, 100);
        var searchResults = await _vectorStore.SearchAsync(
            collection, queryVector, overFetchK, filter: null, ct);

        _logger.LogInformation(
            "Vector search completed. ConversationId={ConversationId}, OverFetchK={OverFetchK}, ResultCount={ResultCount}",
            conversationId, overFetchK, searchResults.Count);

        // 2.5. Pre-filter by MinScore
        var candidates = FilterByScore(searchResults, _ragOptions.MinScore);

        _logger.LogInformation(
            "Pre-filter applied. ConversationId={ConversationId}, Before={Before}, After={After}, MinScore={MinScore}",
            conversationId, searchResults.Count, candidates.Count, _ragOptions.MinScore);

        // 3. Re-rank with keyword boost and take desired TopK
        var keywords = ExtractKeywords(request.Query);
        var expanded = ExpandKeywords(keywords);
        var reranked = RerankWithKeywordBoost(candidates, expanded);
        var filtered = reranked.Take(request.TopK).ToList();

        _logger.LogInformation(
            "Keyword re-ranking applied. ConversationId={ConversationId}, Keywords=[{Keywords}], TopK={TopK}, ResultCount={ResultCount}",
            conversationId, string.Join(", ", keywords), request.TopK, filtered.Count);

        // 4. Map results to response
        return new RagResponse
        {
            ConversationId = conversationId,
            PipelineMode = RagPipelineMode.Naive,
            Results = MapToRetrievalResults(filtered)
        };
    }

    // ─── 2세대: Advanced RAG ────────────────────────────────────────────

    private async Task<RagResponse> RunAdvancedPipelineAsync(
        RagRequest request, string conversationId, CancellationToken ct)
    {
        _logger.LogDebug("Running Advanced pipeline. ConversationId={ConversationId}", conversationId);

        // 1. Pre-retrieval: Rewrite query
        var rewrittenQuery = request.Query;
        if (_queryRewriter is not null && _ragOptions.EnableQueryRewriting)
        {
            rewrittenQuery = await _queryRewriter.RewriteAsync(request.Query, ct);
            _logger.LogInformation(
                "Query rewritten. ConversationId={ConversationId}, Original={Original}, Rewritten={Rewritten}",
                conversationId, request.Query, rewrittenQuery);
        }

        // 2. Embed the rewritten query
        var queryVector = await _llmClient.GetEmbeddingAsync(rewrittenQuery, ct);

        // 3. Over-fetch candidates
        var collection = _qdrantOptions.DefaultCollection;
        var overFetchK = Math.Max(_ragOptions.LlmRerankCandidateCount, request.TopK * 10);
        var searchResults = await _vectorStore.SearchAsync(
            collection, queryVector, overFetchK, filter: null, ct);

        // 4. Pre-filter by MinScore
        var candidates = FilterByScore(searchResults, _ragOptions.MinScore);

        _logger.LogInformation(
            "Advanced vector search. ConversationId={ConversationId}, OverFetchK={OverFetchK}, PreFilter={PreFilter}, PostFilter={PostFilter}",
            conversationId, overFetchK, searchResults.Count, candidates.Count);

        // 5. Post-retrieval: LLM reranking
        List<VectorSearchResult> finalResults;
        if (_llmReranker is not null && _ragOptions.EnableLlmReranking)
        {
            finalResults = await _llmReranker.RerankAsync(request.Query, candidates, request.TopK, ct);
        }
        else
        {
            // Fallback to keyword reranking
            var keywords = ExtractKeywords(request.Query);
            var expanded = ExpandKeywords(keywords);
            finalResults = RerankWithKeywordBoost(candidates, expanded).Take(request.TopK).ToList();
        }

        return new RagResponse
        {
            ConversationId = conversationId,
            PipelineMode = RagPipelineMode.Advanced,
            RewrittenQuery = rewrittenQuery,
            Results = MapToRetrievalResults(finalResults)
        };
    }

    // ─── 3세대: GraphRAG ────────────────────────────────────────────────

    private async Task<RagResponse> RunGraphPipelineAsync(
        RagRequest request, string conversationId, CancellationToken ct)
    {
        _logger.LogDebug("Running Graph pipeline. ConversationId={ConversationId}", conversationId);

        // 1. Rewrite query (reuse Advanced rewriter)
        var rewrittenQuery = request.Query;
        if (_queryRewriter is not null && _ragOptions.EnableQueryRewriting)
        {
            rewrittenQuery = await _queryRewriter.RewriteAsync(request.Query, ct);
        }

        // 2. Embed + vector search
        var queryVector = await _llmClient.GetEmbeddingAsync(rewrittenQuery, ct);
        var collection = _qdrantOptions.DefaultCollection;
        var overFetchK = Math.Max(_ragOptions.LlmRerankCandidateCount, request.TopK * 10);
        var searchResults = await _vectorStore.SearchAsync(
            collection, queryVector, overFetchK, filter: null, ct);
        var candidates = FilterByScore(searchResults, _ragOptions.MinScore);

        // 3. Graph lookup: extract keywords → get related entities → build context
        string? graphContext = null;
        if (_graphStore is not null && (request.EnableGraphLookup || _ragOptions.EnableGraphLookup))
        {
            var keywords = ExtractKeywords(request.Query);
            graphContext = await _graphStore.BuildGraphContextAsync(
                request.Query, keywords, ct);

            _logger.LogInformation(
                "Graph context built. ConversationId={ConversationId}, ContextLength={Length}",
                conversationId, graphContext?.Length ?? 0);
        }

        // 4. LLM rerank
        List<VectorSearchResult> finalResults;
        if (_llmReranker is not null && _ragOptions.EnableLlmReranking)
        {
            finalResults = await _llmReranker.RerankAsync(request.Query, candidates, request.TopK, ct);
        }
        else
        {
            var keywords = ExtractKeywords(request.Query);
            var expanded = ExpandKeywords(keywords);
            finalResults = RerankWithKeywordBoost(candidates, expanded).Take(request.TopK).ToList();
        }

        // 5. Map results and attach graph context
        var results = MapToRetrievalResults(finalResults);
        if (!string.IsNullOrEmpty(graphContext))
        {
            foreach (var result in results)
            {
                result.GraphContext = graphContext;
            }
        }

        return new RagResponse
        {
            ConversationId = conversationId,
            PipelineMode = RagPipelineMode.Graph,
            RewrittenQuery = rewrittenQuery,
            Results = results
        };
    }

    // ─── 4세대: Agentic RAG ────────────────────────────────────────────

    private async Task<RagResponse> RunAgenticPipelineAsync(
        RagRequest request, string conversationId, CancellationToken ct)
    {
        _logger.LogDebug("Running Agentic pipeline. ConversationId={ConversationId}", conversationId);

        if (_agenticOrchestrator is null)
        {
            _logger.LogWarning(
                "Agentic orchestrator not available, falling back to Graph pipeline. ConversationId={ConversationId}",
                conversationId);
            return await RunGraphPipelineAsync(request, conversationId, ct);
        }

        var (results, iterationCount) = await _agenticOrchestrator.OrchestrateAsync(request, ct);

        _logger.LogInformation(
            "Agentic pipeline completed. ConversationId={ConversationId}, Iterations={Iterations}, ResultCount={ResultCount}",
            conversationId, iterationCount, results.Count);

        return new RagResponse
        {
            ConversationId = conversationId,
            PipelineMode = RagPipelineMode.Agentic,
            IterationCount = iterationCount,
            Results = results
        };
    }

    // ─── Shared helpers ─────────────────────────────────────────────────

    private static List<RetrievalResult> MapToRetrievalResults(IReadOnlyList<VectorSearchResult> results)
    {
        return results.Select(r => new RetrievalResult
        {
            DocumentId = r.Id,
            ChunkText = r.Payload.TryGetValue("text", out var text) ? text.ToString() ?? string.Empty : string.Empty,
            Score = r.Score,
            Metadata = new Dictionary<string, object>(r.Payload)
        }).ToList();
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
