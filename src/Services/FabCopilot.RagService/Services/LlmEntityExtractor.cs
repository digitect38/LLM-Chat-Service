using System.Text.Json;
using FabCopilot.Contracts.Models;
using FabCopilot.Llm.Interfaces;
using FabCopilot.Llm.Models;
using FabCopilot.RagService.Interfaces;
using Microsoft.Extensions.Logging;

namespace FabCopilot.RagService.Services;

public sealed class LlmEntityExtractor : IEntityExtractor
{
    private const string EntityExtractionPrompt = """
        당신은 반도체 FAB 장비 도메인 전문가입니다.
        주어진 텍스트에서 엔티티(개체)를 추출하세요.

        엔티티 타입:
        - Equipment: 장비 (예: CMP, Etcher, CVD)
        - Component: 부품/구성요소 (예: polishing pad, slurry, wafer carrier)
        - Process: 공정/프로세스 (예: planarization, etching, deposition)
        - Symptom: 증상/현상 (예: scratch, non-uniformity, particle)
        - Cause: 원인 (예: pad wear, slurry degradation, pressure imbalance)

        반드시 JSON 형식으로만 응답하세요:
        {"entities": [{"name": "이름", "type": "타입"}]}

        설명이나 부가 텍스트 없이 JSON만 출력하세요.
        """;

    private const string RelationExtractionPrompt = """
        당신은 반도체 FAB 장비 도메인 전문가입니다.
        주어진 텍스트와 엔티티 목록을 바탕으로 엔티티 간의 관계를 추출하세요.

        관계 타입:
        - HasComponent: A가 B를 구성요소로 가짐
        - CausedBy: A가 B에 의해 발생됨
        - RelatesTo: A가 B와 관련됨
        - UsedIn: A가 B에 사용됨
        - Produces: A가 B를 생성/유발함

        반드시 JSON 형식으로만 응답하세요:
        {"relations": [{"source": "소스이름", "target": "타겟이름", "type": "관계타입"}]}

        설명이나 부가 텍스트 없이 JSON만 출력하세요.
        """;

    private readonly ILlmClient _llmClient;
    private readonly ILogger<LlmEntityExtractor> _logger;

    public LlmEntityExtractor(ILlmClient llmClient, ILogger<LlmEntityExtractor> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<List<GraphEntity>> ExtractEntitiesAsync(string text, CancellationToken ct)
    {
        try
        {
            // Truncate very long texts
            var input = text.Length > 2000 ? text[..2000] : text;

            var messages = new List<LlmChatMessage>
            {
                LlmChatMessage.System(EntityExtractionPrompt),
                LlmChatMessage.User(input)
            };

            var response = await _llmClient.CompleteChatAsync(messages, null, ct);
            return ParseEntities(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Entity extraction failed");
            return [];
        }
    }

    public async Task<List<GraphRelation>> ExtractRelationsAsync(
        string text, List<GraphEntity> entities, CancellationToken ct)
    {
        if (entities.Count == 0)
            return [];

        try
        {
            var input = text.Length > 2000 ? text[..2000] : text;
            var entityList = string.Join(", ", entities.Select(e => $"{e.Name}({e.Type})"));

            var messages = new List<LlmChatMessage>
            {
                LlmChatMessage.System(RelationExtractionPrompt),
                LlmChatMessage.User($"텍스트: {input}\n\n엔티티 목록: {entityList}")
            };

            var response = await _llmClient.CompleteChatAsync(messages, null, ct);
            return ParseRelations(response, entities);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Relation extraction failed");
            return [];
        }
    }

    private static string StripCodeFences(string text)
    {
        if (text.Contains("```"))
            return System.Text.RegularExpressions.Regex.Replace(text, @"```(?:json)?\s*", "");
        return text;
    }

    private List<GraphEntity> ParseEntities(string response)
    {
        try
        {
            var cleaned = StripCodeFences(response);
            var jsonStart = cleaned.IndexOf('{');
            var jsonEnd = cleaned.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < 0)
                return [];

            var json = cleaned[jsonStart..(jsonEnd + 1)];
            var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("entities", out var entitiesArray))
                return [];

            var result = new List<GraphEntity>();
            foreach (var elem in entitiesArray.EnumerateArray())
            {
                var name = elem.GetProperty("name").GetString() ?? "";
                var type = elem.GetProperty("type").GetString() ?? "";
                if (!string.IsNullOrEmpty(name))
                {
                    result.Add(new GraphEntity
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = name,
                        Type = type
                    });
                }
            }

            _logger.LogDebug("Extracted {Count} entities", result.Count);
            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse entity extraction response");
            return [];
        }
    }

    private List<GraphRelation> ParseRelations(string response, List<GraphEntity> entities)
    {
        try
        {
            var cleaned = StripCodeFences(response);
            var jsonStart = cleaned.IndexOf('{');
            var jsonEnd = cleaned.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < 0)
                return [];

            var json = cleaned[jsonStart..(jsonEnd + 1)];
            var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("relations", out var relationsArray))
                return [];

            var entityLookup = entities.ToDictionary(
                e => e.Name, e => e.Id, StringComparer.OrdinalIgnoreCase);

            var result = new List<GraphRelation>();
            foreach (var elem in relationsArray.EnumerateArray())
            {
                var source = elem.GetProperty("source").GetString() ?? "";
                var target = elem.GetProperty("target").GetString() ?? "";
                var type = elem.GetProperty("type").GetString() ?? "";

                if (!string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(target))
                {
                    result.Add(new GraphRelation
                    {
                        SourceId = entityLookup.GetValueOrDefault(source, source),
                        TargetId = entityLookup.GetValueOrDefault(target, target),
                        RelationType = type
                    });
                }
            }

            _logger.LogDebug("Extracted {Count} relations", result.Count);
            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse relation extraction response");
            return [];
        }
    }
}
