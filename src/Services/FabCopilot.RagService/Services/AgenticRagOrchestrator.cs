using System.Text.Json;
using FabCopilot.Contracts.Enums;
using FabCopilot.Contracts.Messages;
using FabCopilot.Llm.Interfaces;
using FabCopilot.Llm.Models;
using FabCopilot.RagService.Configuration;
using FabCopilot.RagService.Interfaces;
using FabCopilot.VectorStore.Configuration;
using FabCopilot.VectorStore.Interfaces;
using FabCopilot.VectorStore.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FabCopilot.RagService.Services;

public sealed class AgenticRagOrchestrator : IAgenticRagOrchestrator
{
    private const string PlanPrompt = """
        당신은 반도체 FAB 장비 도메인의 질문 분석 전문가입니다.
        사용자의 질문을 분석하여 답변에 필요한 정보를 검색하기 위한 하위 질문들로 분해하세요.

        규칙:
        1. 각 하위 질문은 독립적으로 검색 가능해야 합니다.
        2. 하위 질문은 최대 3개까지 생성하세요.
        3. 반드시 JSON 배열 형식으로만 응답하세요.

        예시 응답: ["CMP 패드 교체 시기 기준은?", "CMP 패드 마모 측정 방법은?"]
        """;

    private const string VerifyPrompt = """
        당신은 반도체 FAB 장비 도메인 전문가입니다.
        원래 질문과 수집된 컨텍스트를 보고, 질문에 충분히 답변할 수 있는 정보가 모였는지 평가하세요.

        규칙:
        1. 충분하면 "SUFFICIENT"로만 응답하세요.
        2. 부족하면 추가로 검색할 하위 질문을 JSON 배열로 응답하세요.
        예시: ["추가 검색 질문 1", "추가 검색 질문 2"]
        """;

    private readonly ILlmClient _llmClient;
    private readonly IVectorStore _vectorStore;
    private readonly QdrantOptions _qdrantOptions;
    private readonly RagOptions _ragOptions;
    private readonly IQueryRewriter? _queryRewriter;
    private readonly ILlmReranker? _llmReranker;
    private readonly IKnowledgeGraphStore? _graphStore;
    private readonly ILogger<AgenticRagOrchestrator> _logger;

    public AgenticRagOrchestrator(
        ILlmClient llmClient,
        IVectorStore vectorStore,
        IOptions<QdrantOptions> qdrantOptions,
        IOptions<RagOptions> ragOptions,
        ILogger<AgenticRagOrchestrator> logger,
        IQueryRewriter? queryRewriter = null,
        ILlmReranker? llmReranker = null,
        IKnowledgeGraphStore? graphStore = null)
    {
        _llmClient = llmClient;
        _vectorStore = vectorStore;
        _qdrantOptions = qdrantOptions.Value;
        _ragOptions = ragOptions.Value;
        _logger = logger;
        _queryRewriter = queryRewriter;
        _llmReranker = llmReranker;
        _graphStore = graphStore;
    }

    public async Task<(List<RetrievalResult> Results, int IterationCount)> OrchestrateAsync(
        RagRequest request, CancellationToken ct)
    {
        var maxIterations = request.MaxAgenticIterations > 0
            ? request.MaxAgenticIterations
            : _ragOptions.DefaultMaxAgenticIterations;

        var allResults = new List<RetrievalResult>();
        var iteration = 0;

        // Step 1: Plan — decompose query into sub-questions
        var subQuestions = await PlanAsync(request.Query, ct);
        _logger.LogInformation(
            "Agentic Plan: decomposed into {Count} sub-questions", subQuestions.Count);

        while (iteration < maxIterations)
        {
            iteration++;
            _logger.LogInformation("Agentic iteration {Iteration}/{Max}", iteration, maxIterations);

            // Step 2: Act — retrieve for each sub-question
            foreach (var subQ in subQuestions)
            {
                var results = await RetrieveForSubQuestionAsync(subQ, request, ct);
                allResults.AddRange(results);
            }

            // Deduplicate by DocumentId
            allResults = allResults
                .GroupBy(r => r.DocumentId)
                .Select(g => g.OrderByDescending(r => r.Score).First())
                .ToList();

            // Step 3: Verify — check if context is sufficient
            var followUp = await VerifyAsync(request.Query, allResults, ct);
            if (followUp.Count == 0)
            {
                _logger.LogInformation(
                    "Agentic: context sufficient after {Iteration} iterations", iteration);
                break;
            }

            _logger.LogInformation(
                "Agentic: context insufficient, {Count} follow-up questions generated", followUp.Count);
            subQuestions = followUp;
        }

        // Take TopK from final results
        var finalResults = allResults
            .OrderByDescending(r => r.Score)
            .Take(request.TopK)
            .ToList();

        return (finalResults, iteration);
    }

    private async Task<List<string>> PlanAsync(string query, CancellationToken ct)
    {
        try
        {
            var messages = new List<LlmChatMessage>
            {
                LlmChatMessage.System(PlanPrompt),
                LlmChatMessage.User(query)
            };

            var response = await _llmClient.CompleteChatAsync(messages, null, ct);
            return ParseStringArray(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agentic Plan failed, using original query as single sub-question");
            return [query];
        }
    }

    private async Task<List<string>> VerifyAsync(
        string originalQuery, List<RetrievalResult> results, CancellationToken ct)
    {
        try
        {
            var contextSummary = string.Join("\n",
                results.Select((r, i) => $"[{i}] {r.ChunkText[..Math.Min(r.ChunkText.Length, 300)]}"));

            var messages = new List<LlmChatMessage>
            {
                LlmChatMessage.System(VerifyPrompt),
                LlmChatMessage.User($"원래 질문: {originalQuery}\n\n수집된 컨텍스트:\n{contextSummary}")
            };

            var response = await _llmClient.CompleteChatAsync(messages, null, ct);

            if (response.Contains("SUFFICIENT", StringComparison.OrdinalIgnoreCase))
                return [];

            return ParseStringArray(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agentic Verify failed, treating context as sufficient");
            return [];
        }
    }

    private async Task<List<RetrievalResult>> RetrieveForSubQuestionAsync(
        string subQuestion, RagRequest originalRequest, CancellationToken ct)
    {
        // Optionally rewrite the sub-question
        var searchQuery = subQuestion;
        if (_queryRewriter is not null && _ragOptions.EnableQueryRewriting)
        {
            searchQuery = await _queryRewriter.RewriteAsync(subQuestion, ct);
        }

        // Embed and search
        var queryVector = await _llmClient.GetEmbeddingAsync(searchQuery, isQuery: true, ct);
        var collection = _qdrantOptions.DefaultCollection;
        var overFetchK = Math.Max(_ragOptions.LlmRerankCandidateCount, originalRequest.TopK * 5);
        var searchResults = await _vectorStore.SearchAsync(
            collection, queryVector, overFetchK, filter: null, ct);

        // Filter by score
        var candidates = searchResults.Where(r => r.Score >= _ragOptions.MinScore).ToList();

        // Graph lookup if available
        string? graphContext = null;
        if (_graphStore is not null && _ragOptions.EnableGraphLookup)
        {
            var keywords = RagWorker.ExtractKeywords(subQuestion);
            graphContext = await _graphStore.BuildGraphContextAsync(subQuestion, keywords, ct);
        }

        // LLM rerank or keyword rerank
        List<VectorSearchResult> finalCandidates;
        if (_llmReranker is not null && _ragOptions.EnableLlmReranking)
        {
            finalCandidates = await _llmReranker.RerankAsync(
                subQuestion, candidates, originalRequest.TopK, ct);
        }
        else
        {
            var keywords = RagWorker.ExtractKeywords(subQuestion);
            var expanded = RagWorker.ExpandKeywords(keywords);
            finalCandidates = RagWorker.RerankWithKeywordBoost(candidates, expanded)
                .Take(originalRequest.TopK).ToList();
        }

        // Map to RetrievalResult
        return finalCandidates.Select(r =>
        {
            var result = new RetrievalResult
            {
                DocumentId = r.Id,
                ChunkText = r.Payload.TryGetValue("text", out var text)
                    ? text.ToString() ?? string.Empty
                    : string.Empty,
                Score = r.Score,
                Metadata = new Dictionary<string, object>(r.Payload),
                GraphContext = graphContext
            };
            return result;
        }).ToList();
    }

    private static List<string> ParseStringArray(string response)
    {
        try
        {
            var jsonStart = response.IndexOf('[');
            var jsonEnd = response.LastIndexOf(']');
            if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
                return [];

            var json = response[jsonStart..(jsonEnd + 1)];
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
