using System.Text.Json;
using FabCopilot.Llm.Interfaces;
using FabCopilot.Llm.Models;
using FabCopilot.RagService.Interfaces;
using FabCopilot.VectorStore.Models;
using Microsoft.Extensions.Logging;

namespace FabCopilot.RagService.Services;

public sealed class LlmReranker : ILlmReranker
{
    private const string SystemPrompt = """
        당신은 문서 관련성 평가 전문가입니다.
        사용자 질문과 후보 문서 목록이 주어집니다.
        각 문서의 질문에 대한 관련성을 0~10 점수로 평가하세요.

        반드시 JSON 배열 형식으로만 응답하세요. 각 요소는 {"index": 번호, "score": 점수} 형태입니다.
        설명이나 부가 텍스트 없이 JSON 배열만 출력하세요.

        예시 응답: [{"index":0,"score":9},{"index":1,"score":3},{"index":2,"score":7}]
        """;

    private readonly ILlmClient _llmClient;
    private readonly ILogger<LlmReranker> _logger;

    public LlmReranker(ILlmClient llmClient, ILogger<LlmReranker> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<List<VectorSearchResult>> RerankAsync(
        string query,
        IReadOnlyList<VectorSearchResult> candidates,
        int topK,
        CancellationToken ct)
    {
        if (candidates.Count == 0)
            return [];

        if (candidates.Count <= topK)
            return candidates.ToList();

        try
        {
            var userPrompt = BuildUserPrompt(query, candidates);
            var messages = new List<LlmChatMessage>
            {
                LlmChatMessage.System(SystemPrompt),
                LlmChatMessage.User(userPrompt)
            };

            var response = await _llmClient.CompleteChatAsync(messages, null, ct);
            var ranked = ParseRerankResponse(response, candidates);

            if (ranked.Count > 0)
            {
                _logger.LogInformation(
                    "LLM reranking completed. Candidates={CandidateCount}, TopK={TopK}, Ranked={RankedCount}",
                    candidates.Count, topK, ranked.Count);

                return ranked.Take(topK).ToList();
            }

            _logger.LogWarning("LLM reranking returned no valid results, falling back to original order");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM reranking failed, falling back to original order");
        }

        return candidates.Take(topK).ToList();
    }

    private static string BuildUserPrompt(string query, IReadOnlyList<VectorSearchResult> candidates)
    {
        var parts = new List<string> { $"질문: {query}\n\n후보 문서:" };

        for (var i = 0; i < candidates.Count; i++)
        {
            var text = candidates[i].Payload.TryGetValue("text", out var t)
                ? t.ToString() ?? ""
                : "";
            // Truncate long chunks for the reranking prompt
            if (text.Length > 500)
                text = text[..500] + "...";
            parts.Add($"\n[{i}] {text}");
        }

        return string.Join("", parts);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private List<VectorSearchResult> ParseRerankResponse(
        string response, IReadOnlyList<VectorSearchResult> candidates)
    {
        try
        {
            // Strip markdown code fences if present (```json ... ``` or ``` ... ```)
            var cleaned = response;
            if (cleaned.Contains("```"))
            {
                cleaned = System.Text.RegularExpressions.Regex.Replace(
                    cleaned, @"```(?:json)?\s*", "");
            }

            // Extract JSON array from response (may contain surrounding text)
            var jsonStart = cleaned.IndexOf('[');
            var jsonEnd = cleaned.LastIndexOf(']');
            if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
            {
                _logger.LogWarning("No JSON array found in LLM rerank response: {Response}", response);
                return [];
            }

            var json = cleaned[jsonStart..(jsonEnd + 1)];
            var scores = JsonSerializer.Deserialize<List<RerankScore>>(json, JsonOptions);
            if (scores is null || scores.Count == 0)
            {
                _logger.LogWarning("Deserialized empty scores from LLM rerank response: {Json}", json);
                return [];
            }

            _logger.LogDebug("Parsed {Count} rerank scores from LLM response", scores.Count);

            return scores
                .Where(s => s.Index >= 0 && s.Index < candidates.Count)
                .OrderByDescending(s => s.Score)
                .Select(s => candidates[s.Index])
                .ToList();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM rerank response: {Response}", response);
            return [];
        }
    }

    private sealed class RerankScore
    {
        public int Index { get; set; }
        public float Score { get; set; }
    }
}
