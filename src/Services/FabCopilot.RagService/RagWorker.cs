using System.Diagnostics;
using System.Text.RegularExpressions;
using FabCopilot.Contracts.Constants;
using FabCopilot.Contracts.Enums;
using FabCopilot.Contracts.Messages;
using FabCopilot.Llm.Interfaces;
using FabCopilot.Messaging.Interfaces;
using FabCopilot.Observability.Metrics;
using FabCopilot.RagService.Configuration;
using FabCopilot.RagService.Interfaces;
using FabCopilot.RagService.Services;
using FabCopilot.RagService.Services.Bm25;
using FabCopilot.VectorStore.Configuration;
using FabCopilot.VectorStore.Interfaces;
using FabCopilot.VectorStore.Models;
using Microsoft.Extensions.Options;

// Alias for clarity
using ISynonymDict = FabCopilot.RagService.Interfaces.ISynonymDictionary;

namespace FabCopilot.RagService;

public sealed class RagWorker : BackgroundService
{
    private const string QueueGroup = "rag-workers";

    private readonly IMessageBus _messageBus;
    private readonly ILlmClient _llmClient;
    private readonly IVectorStore _vectorStore;
    private readonly IBm25Index? _bm25Index;
    private readonly ISynonymDict? _synonymDictionary;
    private readonly IRagCache? _ragCache;
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
        IBm25Index? bm25Index = null,
        ISynonymDict? synonymDictionary = null,
        IRagCache? ragCache = null,
        IQueryRewriter? queryRewriter = null,
        ILlmReranker? llmReranker = null,
        IKnowledgeGraphStore? graphStore = null,
        IAgenticRagOrchestrator? agenticOrchestrator = null)
    {
        _messageBus = messageBus;
        _llmClient = llmClient;
        _vectorStore = vectorStore;
        _bm25Index = bm25Index;
        _synonymDictionary = synonymDictionary;
        _ragCache = ragCache;
        _qdrantOptions = qdrantOptions.Value;
        _ragOptions = ragOptions.Value;
        _logger = logger;
        _queryRewriter = queryRewriter;
        _llmReranker = llmReranker;
        _graphStore = graphStore;
        _agenticOrchestrator = agenticOrchestrator;

        _logger.LogInformation(
            "RagWorker initialized. GraphStore={GraphStoreAvailable}, EnableGraphLookup={EnableGraphLookup}, ExtractEntitiesOnIngest={ExtractEntities}",
            _graphStore is not null, _ragOptions.EnableGraphLookup, _ragOptions.ExtractEntitiesOnIngest);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "RagWorker started. Subscribing to {Subject} with queue group {QueueGroup}. DefaultPipeline={DefaultPipeline}",
            NatsSubjects.RagRequest, QueueGroup, _ragOptions.DefaultPipelineMode);

        // Rebuild keyword index for entities ingested before keyword indexing was added
        if (_graphStore is not null && _ragOptions.EnableGraphLookup)
        {
            try
            {
                await _graphStore.RebuildKeywordIndexAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to rebuild graph keyword index on startup");
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
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

                // Subscription completed (NATS idle timeout) — re-subscribe
                _logger.LogWarning("NATS subscription completed, re-subscribing to {Subject}", NatsSubjects.RagRequest);
                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("RagWorker stopped");
    }

    private async Task ProcessRagRequestAsync(RagRequest request, CancellationToken ct)
    {
        var conversationId = request.ConversationId ?? Guid.NewGuid().ToString();
        var responseSubject = NatsSubjects.RagResponse(conversationId);

        _logger.LogInformation(
            "Processing RAG request. ConversationId={ConversationId}, Query={Query}, EquipmentId={EquipmentId}, TopK={TopK}, Pipeline={Pipeline}",
            conversationId, request.Query, request.EquipmentId, request.TopK, request.PipelineMode);

        try
        {
            var pipelineMode = request.PipelineMode;

            // Classify query intent for routing
            var queryIntent = QueryRouter.Classify(request.Query);
            _logger.LogInformation(
                "Query intent classified. ConversationId={ConversationId}, Intent={Intent}",
                conversationId, queryIntent);

            // Check cache first
            var pipelineTimer = FabMetrics.StartTimer();
            RagResponse? response = null;
            var pipelineModeStr = pipelineMode.ToString();
            if (_ragCache is not null)
            {
                response = await _ragCache.GetAsync(request.Query, request.EquipmentId, pipelineModeStr, request.TopK, ct);
                if (response is not null)
                {
                    response.ConversationId = conversationId;
                    FabMetrics.RagCacheHits.Add(1);
                    _logger.LogInformation(
                        "RAG cache hit. ConversationId={ConversationId}, ResultCount={ResultCount}",
                        conversationId, response.Results.Count);
                }
            }

            // Cache miss — run pipeline
            if (response is null)
            {
                if (_ragCache is not null)
                    FabMetrics.RagCacheMisses.Add(1);

                response = pipelineMode switch
                {
                    RagPipelineMode.Advanced => await RunAdvancedPipelineAsync(request, conversationId, ct),
                    RagPipelineMode.Graph => await RunGraphPipelineAsync(request, conversationId, ct),
                    RagPipelineMode.Agentic => await RunAgenticPipelineAsync(request, conversationId, ct),
                    _ => await RunNaivePipelineAsync(request, conversationId, ct)
                };

                // Store in cache
                if (_ragCache is not null)
                {
                    await _ragCache.SetAsync(request.Query, request.EquipmentId, pipelineModeStr, request.TopK, response, ct);
                }
            }

            FabMetrics.RecordElapsed(pipelineTimer, FabMetrics.RagPipelineDuration);

            // Attach intent and confidence info
            response.QueryIntent = queryIntent;
            response.MaxScore = response.Results.Count > 0
                ? response.Results.Max(r => r.Score)
                : 0f;
            response.IsConfident = response.MaxScore >= _ragOptions.MinScore;

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
        var queryVector = await _llmClient.GetEmbeddingAsync(request.Query, isQuery: true, ct);

        // 2. Over-fetch candidates from vector store
        var vectorSw = FabMetrics.StartTimer();
        var collection = _qdrantOptions.DefaultCollection;
        var overFetchK = Math.Max(request.TopK * 10, 100);
        var searchResults = await _vectorStore.SearchAsync(
            collection, queryVector, overFetchK, filter: null, ct);
        var vectorMs = FabMetrics.RecordElapsed(vectorSw, FabMetrics.RagVectorSearchDuration);

        _logger.LogInformation(
            "Vector search completed. ConversationId={ConversationId}, OverFetchK={OverFetchK}, ResultCount={ResultCount}, ElapsedMs={ElapsedMs:F1}",
            conversationId, overFetchK, searchResults.Count, vectorMs);

        // 2.5. Pre-filter by MinScore
        var candidates = FilterByScore(searchResults, _ragOptions.MinScore);

        _logger.LogInformation(
            "Pre-filter applied. ConversationId={ConversationId}, Before={Before}, After={After}, MinScore={MinScore}",
            conversationId, searchResults.Count, candidates.Count, _ragOptions.MinScore);

        // 2.6. Hybrid merge with BM25 via RRF
        candidates = HybridMerge(candidates, request.Query, request.TopK);

        _logger.LogInformation(
            "Hybrid search merge applied. ConversationId={ConversationId}, CandidateCount={CandidateCount}",
            conversationId, candidates.Count);

        // 3. Re-rank with keyword boost
        var rerankSw = FabMetrics.StartTimer();
        var keywords = ExtractKeywords(request.Query);
        var expanded = ExpandKeywordsWithSynonyms(keywords);
        var reranked = RerankWithKeywordBoost(candidates, expanded);
        FabMetrics.RecordElapsed(rerankSw, FabMetrics.RagRerankDuration);

        // 3.5. MMR diversity selection
        var filtered = ApplyMmr(reranked, request.Query, request.TopK);

        _logger.LogInformation(
            "Keyword re-ranking + MMR applied. ConversationId={ConversationId}, Keywords=[{Keywords}], TopK={TopK}, ResultCount={ResultCount}",
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

        // 1. Pre-retrieval: Rewrite query (with timeout + graceful degradation)
        var rewrittenQuery = request.Query;
        if (_queryRewriter is not null && _ragOptions.EnableQueryRewriting)
        {
            try
            {
                using var rewriteCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                rewriteCts.CancelAfter(_ragOptions.QueryRewriteTimeoutMs);
                var rewriteSw = FabMetrics.StartTimer();
                rewrittenQuery = await _queryRewriter.RewriteAsync(request.Query, rewriteCts.Token);
                FabMetrics.RecordElapsed(rewriteSw, FabMetrics.RagQueryRewriteDuration);
                _logger.LogInformation(
                    "Query rewritten. ConversationId={ConversationId}, Original={Original}, Rewritten={Rewritten}",
                    conversationId, request.Query, rewrittenQuery);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Query rewrite timed out ({TimeoutMs}ms), using original query. ConversationId={ConversationId}",
                    _ragOptions.QueryRewriteTimeoutMs, conversationId);
                FabMetrics.RagStageTimeoutCount.Add(1, new KeyValuePair<string, object?>("stage", "query_rewrite"));
                rewrittenQuery = request.Query;
            }
        }

        // 2. Embed the rewritten query
        var queryVector = await _llmClient.GetEmbeddingAsync(rewrittenQuery, isQuery: true, ct);

        // 3. Over-fetch candidates (with timeout)
        IReadOnlyList<VectorSearchResult> searchResults;
        {
            using var vectorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            vectorCts.CancelAfter(_ragOptions.VectorSearchTimeoutMs);
            var advVectorSw = FabMetrics.StartTimer();
            var collection = _qdrantOptions.DefaultCollection;
            var overFetchK = Math.Max(_ragOptions.LlmRerankCandidateCount, request.TopK * 10);
            searchResults = await _vectorStore.SearchAsync(
                collection, queryVector, overFetchK, filter: null, vectorCts.Token);
            FabMetrics.RecordElapsed(advVectorSw, FabMetrics.RagVectorSearchDuration);
        }

        // 4. Pre-filter by MinScore
        var candidates = FilterByScore(searchResults, _ragOptions.MinScore);

        _logger.LogInformation(
            "Advanced vector search. ConversationId={ConversationId}, OverFetchK={OverFetchK}, PreFilter={PreFilter}, PostFilter={PostFilter}",
            conversationId, Math.Max(_ragOptions.LlmRerankCandidateCount, request.TopK * 10), searchResults.Count, candidates.Count);

        // 4.5. Hybrid merge with BM25 via RRF
        candidates = HybridMerge(candidates, rewrittenQuery, request.TopK);

        // 5. Post-retrieval: LLM reranking (with timeout + graceful fallback)
        var advRerankSw = FabMetrics.StartTimer();
        List<VectorSearchResult> reranked;
        if (_llmReranker is not null && _ragOptions.EnableLlmReranking)
        {
            try
            {
                using var rerankCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                rerankCts.CancelAfter(_ragOptions.LlmRerankTimeoutMs);
                reranked = await _llmReranker.RerankAsync(request.Query, candidates, request.TopK * 2, rerankCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "LLM rerank timed out ({TimeoutMs}ms), falling back to keyword boost. ConversationId={ConversationId}",
                    _ragOptions.LlmRerankTimeoutMs, conversationId);
                FabMetrics.RagStageTimeoutCount.Add(1, new KeyValuePair<string, object?>("stage", "llm_rerank"));
                var keywords = ExtractKeywords(request.Query);
                var expanded = ExpandKeywordsWithSynonyms(keywords);
                reranked = RerankWithKeywordBoost(candidates, expanded);
            }
        }
        else
        {
            // Fallback to keyword reranking
            var keywords = ExtractKeywords(request.Query);
            var expanded = ExpandKeywordsWithSynonyms(keywords);
            reranked = RerankWithKeywordBoost(candidates, expanded);
        }
        FabMetrics.RecordElapsed(advRerankSw, FabMetrics.RagRerankDuration);

        // 5.5. MMR diversity selection
        var finalResults = ApplyMmr(reranked, request.Query, request.TopK);

        // 6. Graph lookup: enrich results with knowledge graph context
        var results = MapToRetrievalResults(finalResults);
        if (_graphStore is not null && _ragOptions.EnableGraphLookup)
        {
            try
            {
                var keywords = ExtractKeywords(request.Query);
                var graphContext = await _graphStore.BuildGraphContextAsync(
                    request.Query, keywords, ct);

                _logger.LogInformation(
                    "Graph context built. ConversationId={ConversationId}, Keywords=[{Keywords}], ContextLength={Length}",
                    conversationId, string.Join(", ", keywords.Take(5)), graphContext?.Length ?? 0);

                if (!string.IsNullOrEmpty(graphContext))
                {
                    foreach (var result in results)
                    {
                        result.GraphContext = graphContext;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Graph lookup failed, continuing without graph context. ConversationId={ConversationId}", conversationId);
            }
        }

        return new RagResponse
        {
            ConversationId = conversationId,
            PipelineMode = RagPipelineMode.Advanced,
            RewrittenQuery = rewrittenQuery,
            Results = results
        };
    }

    // ─── 3세대: GraphRAG ────────────────────────────────────────────────

    private async Task<RagResponse> RunGraphPipelineAsync(
        RagRequest request, string conversationId, CancellationToken ct)
    {
        _logger.LogDebug("Running Graph pipeline. ConversationId={ConversationId}, Query={Query}, TopK={TopK}",
            conversationId, request.Query, request.TopK);

        // 1. Rewrite query (reuse Advanced rewriter)
        var rewrittenQuery = request.Query;
        if (_queryRewriter is not null && _ragOptions.EnableQueryRewriting)
        {
            rewrittenQuery = await _queryRewriter.RewriteAsync(request.Query, ct);
        }

        // 2. Embed + vector search
        var graphVectorSw = FabMetrics.StartTimer();
        var queryVector = await _llmClient.GetEmbeddingAsync(rewrittenQuery, isQuery: true, ct);
        var collection = _qdrantOptions.DefaultCollection;
        var overFetchK = Math.Max(_ragOptions.LlmRerankCandidateCount, request.TopK * 10);
        var searchResults = await _vectorStore.SearchAsync(
            collection, queryVector, overFetchK, filter: null, ct);
        FabMetrics.RecordElapsed(graphVectorSw, FabMetrics.RagVectorSearchDuration);
        var candidates = FilterByScore(searchResults, _ragOptions.MinScore);

        // 2.5. Hybrid merge with BM25 via RRF
        candidates = HybridMerge(candidates, rewrittenQuery, request.TopK);

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
        var graphRerankSw = FabMetrics.StartTimer();
        List<VectorSearchResult> reranked;
        if (_llmReranker is not null && _ragOptions.EnableLlmReranking)
        {
            reranked = await _llmReranker.RerankAsync(request.Query, candidates, request.TopK * 2, ct);
        }
        else
        {
            var keywords = ExtractKeywords(request.Query);
            var expanded = ExpandKeywordsWithSynonyms(keywords);
            reranked = RerankWithKeywordBoost(candidates, expanded);
        }
        FabMetrics.RecordElapsed(graphRerankSw, FabMetrics.RagRerankDuration);

        // 4.5. MMR diversity selection
        var finalResults = ApplyMmr(reranked, request.Query, request.TopK);

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
    /// Expands keywords using the synonym dictionary (if available) or a static fallback map.
    /// </summary>
    internal static readonly Dictionary<string, string[]> FallbackExpansionMap = new()
    {
        ["시기"] = ["기준", "시간", "수명", "주기"],
        ["언제"] = ["기준", "시간", "시점"],
        ["원인"] = ["이유", "문제", "발생"],
        ["방법"] = ["절차", "순서", "단계"],
        ["주기"] = ["기준", "시간", "수명"],
    };

    /// <summary>
    /// Static fallback expansion using the built-in map (used by components without synonym dictionary).
    /// </summary>
    internal static List<string> ExpandKeywords(List<string> keywords)
    {
        var expanded = new HashSet<string>(keywords);
        foreach (var kw in keywords)
        {
            if (FallbackExpansionMap.TryGetValue(kw, out var related))
            {
                foreach (var r in related)
                    expanded.Add(r);
            }
        }
        return expanded.ToList();
    }

    /// <summary>
    /// Instance-level expansion using synonym dictionary when available, falling back to static map.
    /// </summary>
    private List<string> ExpandKeywordsWithSynonyms(List<string> keywords)
    {
        if (_synonymDictionary is not null)
            return _synonymDictionary.ExpandAll(keywords).ToList();

        return ExpandKeywords(keywords);
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
                var boost = Math.Min(matchCount * boostPerKeyword, 0.20f);
                var boostedScore = r.Score + boost;
                return (Result: r, BoostedScore: boostedScore);
            })
            .OrderByDescending(x => x.BoostedScore)
            .Select(x => x.Result)
            .ToList();
    }

    // ─── MMR Diversity ────────────────────────────────────────────────

    private List<VectorSearchResult> ApplyMmr(
        IReadOnlyList<VectorSearchResult> candidates,
        string query,
        int topK)
    {
        if (!_ragOptions.EnableMmr || candidates.Count <= topK)
            return candidates.Take(topK).ToList();

        return MmrSelector.Select(candidates, query, topK, _ragOptions.MmrLambda);
    }

    // ─── Hybrid Search (RRF) ────────────────────────────────────────────

    /// <summary>
    /// Performs hybrid search by combining vector search results with BM25 keyword search
    /// using Reciprocal Rank Fusion (RRF).
    /// </summary>
    private List<VectorSearchResult> HybridMerge(
        IReadOnlyList<VectorSearchResult> vectorResults,
        string query,
        int topK)
    {
        if (_bm25Index is null || !_ragOptions.EnableHybridSearch || !_ragOptions.EnableBm25)
        {
            return vectorResults.ToList();
        }

        // BM25 search — fetch more candidates for better fusion
        var bm25Sw = FabMetrics.StartTimer();
        var bm25Limit = Math.Max(topK * 3, 100);
        var bm25Results = _bm25Index.Search(query, bm25Limit);
        FabMetrics.RecordElapsed(bm25Sw, FabMetrics.RagBm25SearchDuration);

        if (bm25Results.Count == 0)
        {
            _logger.LogDebug("BM25 returned no results, using vector-only results");
            return vectorResults.ToList();
        }

        return MergeWithRrf(vectorResults, bm25Results, _ragOptions.VectorWeight, _ragOptions.Bm25Weight);
    }

    /// <summary>
    /// Merges vector and BM25 results using Reciprocal Rank Fusion.
    /// RRF(d) = vectorWeight/(k+rank_vec) + bm25Weight/(k+rank_bm25), where k=60.
    /// The fused score replaces the original vector score for downstream processing.
    /// </summary>
    internal static List<VectorSearchResult> MergeWithRrf(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<(string DocumentId, double Score)> bm25Results,
        float vectorWeight = 1.0f,
        float bm25Weight = 1.0f,
        int k = 60)
    {
        // Build rank maps (1-based ranks)
        var vectorRanks = new Dictionary<string, int>();
        for (var i = 0; i < vectorResults.Count; i++)
            vectorRanks[vectorResults[i].Id] = i + 1;

        var bm25Ranks = new Dictionary<string, int>();
        for (var i = 0; i < bm25Results.Count; i++)
            bm25Ranks[bm25Results[i].DocumentId] = i + 1;

        // Collect all unique document IDs
        var allDocIds = new HashSet<string>(vectorRanks.Keys);
        allDocIds.UnionWith(bm25Ranks.Keys);

        // Build a lookup for original vector results by ID
        var vectorResultById = vectorResults.ToDictionary(r => r.Id);

        // Calculate RRF scores
        var rrfScored = new List<(VectorSearchResult Result, double RrfScore)>();

        foreach (var docId in allDocIds)
        {
            var vecScore = vectorRanks.TryGetValue(docId, out var vr)
                ? vectorWeight / (k + vr) : 0.0;
            var bm25Score = bm25Ranks.TryGetValue(docId, out var br)
                ? bm25Weight / (k + br) : 0.0;
            var rrfScore = vecScore + bm25Score;

            // Use existing vector result if available, otherwise create a synthetic one from BM25
            if (vectorResultById.TryGetValue(docId, out var result))
            {
                rrfScored.Add((result, rrfScore));
            }
            // Skip BM25-only results that don't have vector data (no payload/text)
        }

        return rrfScored
            .OrderByDescending(x => x.RrfScore)
            .Select(x => x.Result)
            .ToList();
    }
}
