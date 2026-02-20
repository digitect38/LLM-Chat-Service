using FabCopilot.Llm.Interfaces;
using FabCopilot.Llm.Models;
using FabCopilot.RagService.Interfaces;
using Microsoft.Extensions.Logging;

namespace FabCopilot.RagService.Services;

public sealed class LlmQueryRewriter : IQueryRewriter
{
    private const string SystemPrompt = """
        당신은 반도체 FAB 장비 도메인 전문가입니다.
        사용자의 질문을 벡터 검색(embedding search)에 최적화된 형태로 다시 작성하세요.

        규칙:
        1. 약어를 풀어서 작성하세요 (예: CMP → Chemical Mechanical Polishing/화학적 기계적 연마).
        2. 도메인 동의어를 추가하세요 (예: 패드 교체 → 패드 교체 polishing pad replacement 수명 기준).
        3. 핵심 키워드를 명확하게 포함하세요.
        4. 질문의 의도를 유지하면서 검색에 유리한 형태로 변환하세요.
        5. 결과는 다시 작성된 질문 텍스트만 반환하세요. 설명이나 부가 텍스트 없이 질문만 출력하세요.
        """;

    private readonly ILlmClient _llmClient;
    private readonly ILogger<LlmQueryRewriter> _logger;

    public LlmQueryRewriter(ILlmClient llmClient, ILogger<LlmQueryRewriter> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<string> RewriteAsync(string query, CancellationToken ct)
    {
        try
        {
            var messages = new List<LlmChatMessage>
            {
                LlmChatMessage.System(SystemPrompt),
                LlmChatMessage.User(query)
            };

            var rewritten = await _llmClient.CompleteChatAsync(messages, null, ct);
            var result = rewritten.Trim();

            if (string.IsNullOrWhiteSpace(result))
            {
                _logger.LogWarning("Query rewriter returned empty result, using original query");
                return query;
            }

            _logger.LogInformation(
                "Query rewritten. Original={Original}, Rewritten={Rewritten}", query, result);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Query rewriting failed, using original query");
            return query;
        }
    }
}
