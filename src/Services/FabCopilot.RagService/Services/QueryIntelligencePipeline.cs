using System.Text.RegularExpressions;
using FabCopilot.Llm.Interfaces;
using FabCopilot.Llm.Models;
using FabCopilot.RagService.Interfaces;
using Microsoft.Extensions.Logging;

namespace FabCopilot.RagService.Services;

/// <summary>
/// 3-Stage query intelligence pipeline for text/voice input correction.
/// Stage 1: Dictionary Matching (synonym-based typo/abbreviation fix, < 10ms)
/// Stage 2: Pattern-based Correction (error code normalization, unit standardization, < 20ms)
/// Stage 3: LLM Correction (conditional — only if 30%+ tokens remain uncorrected)
/// </summary>
public sealed class QueryIntelligencePipeline
{
    private readonly ISynonymDictionary _synonymDict;
    private readonly ILlmClient _llmClient;
    private readonly ILogger<QueryIntelligencePipeline> _logger;

    // Stage 2: Error code patterns (E1023 → E-1023)
    private static readonly Regex ErrorCodeRaw = new(
        @"\b([A-Z]{1,3})(\d{3,5})\b",
        RegexOptions.Compiled);

    // Stage 2: Unit normalization (100um → 100 μm)
    private static readonly Regex UnitPattern = new(
        @"(\d+(?:\.\d+)?)\s*(um|nm|angstrom|rpm|slm|sccm|psi|torr|pa|mbar)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Stage 2: Korean typo patterns (common input errors in semiconductor domain)
    private static readonly Dictionary<string, string> KoreanTypoMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["슬러이"] = "슬러리",
        ["슬러이펌프"] = "슬러리 펌프",
        ["연마패"] = "연마 패드",
        ["폴리싱패"] = "폴리싱 패드",
        ["웨이퍼스크래치"] = "웨이퍼 스크래치",
        ["콘디셔너"] = "컨디셔너",
        ["플라텐"] = "플래튼",
        ["멤브레인"] = "멤브레인",
        ["멤브래인"] = "멤브레인",
        ["리테이닝"] = "리테이닝",
        ["리테이니"] = "리테이닝",
        ["엔드포인트"] = "엔드포인트",
        ["언드포인트"] = "엔드포인트"
    };

    // Unit normalization map
    private static readonly Dictionary<string, string> UnitNormMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["um"] = "μm",
        ["angstrom"] = "Å",
        ["rpm"] = "RPM",
        ["slm"] = "SLM",
        ["sccm"] = "SCCM",
        ["psi"] = "PSI",
        ["torr"] = "Torr",
        ["pa"] = "Pa",
        ["mbar"] = "mbar",
        ["nm"] = "nm"
    };

    // Common abbreviation expansions
    private static readonly Dictionary<string, string> AbbreviationMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MRR"] = "Material Removal Rate",
        ["CMP"] = "Chemical Mechanical Polishing",
        ["WIWNU"] = "Within-Wafer Non-Uniformity",
        ["EPD"] = "End Point Detection",
        ["PM"] = "Preventive Maintenance",
        ["BM"] = "Breakdown Maintenance",
        ["MTBF"] = "Mean Time Between Failures",
        ["MTTR"] = "Mean Time To Repair",
        ["RUL"] = "Remaining Useful Life",
        ["OCV"] = "Open/Close Valve",
        ["DI"] = "Deionized (Water)"
    };

    // Track correction acceptance for auto-mode suggestion
    private int _totalCorrections;
    private int _acceptedCorrections;

    public QueryIntelligencePipeline(
        ISynonymDictionary synonymDict,
        ILlmClient llmClient,
        ILogger<QueryIntelligencePipeline> logger)
    {
        _synonymDict = synonymDict;
        _llmClient = llmClient;
        _logger = logger;
    }

    /// <summary>
    /// Runs the full 3-stage correction pipeline.
    /// Returns the corrected query and details about what was changed.
    /// </summary>
    public async Task<QueryCorrectionResult> CorrectAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new QueryCorrectionResult(query, query, [], false);

        var corrections = new List<CorrectionDetail>();
        var corrected = query;

        // Stage 1: Dictionary matching (< 10ms)
        corrected = ApplyDictionaryCorrections(corrected, corrections);

        // Stage 2: Pattern-based corrections (< 20ms)
        corrected = ApplyPatternCorrections(corrected, corrections);

        // Stage 3: LLM correction (conditional)
        var uncorrectedRatio = ComputeUncorrectedRatio(query, corrected);
        if (uncorrectedRatio >= 0.3f)
        {
            _logger.LogDebug("Stage 3 triggered: {Ratio:P0} uncorrected tokens", uncorrectedRatio);
            corrected = await ApplyLlmCorrectionAsync(query, corrected, corrections, ct);
        }

        var wasModified = !string.Equals(query, corrected, StringComparison.Ordinal);

        _logger.LogInformation(
            "QueryIntelligence: {StageCount} corrections applied. Modified={Modified}. Original=\"{Original}\" → Corrected=\"{Corrected}\"",
            corrections.Count, wasModified, query, corrected);

        return new QueryCorrectionResult(query, corrected, corrections, wasModified);
    }

    /// <summary>
    /// Stage 1: Dictionary-based matching using SynonymDictionary.
    /// Fixes typos and abbreviations by finding the canonical form.
    /// </summary>
    internal string ApplyDictionaryCorrections(string query, List<CorrectionDetail> corrections)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();
        var modified = false;

        foreach (var word in words)
        {
            var synonyms = _synonymDict.GetSynonyms(word);
            if (synonyms.Count > 0 && !synonyms.Contains(word, StringComparer.OrdinalIgnoreCase))
            {
                // Use the first synonym as canonical form
                var canonical = synonyms[0];
                result.Add(canonical);
                corrections.Add(new CorrectionDetail(1, "Dictionary", word, canonical));
                modified = true;
            }
            else
            {
                result.Add(word);
            }
        }

        return modified ? string.Join(' ', result) : query;
    }

    /// <summary>
    /// Stage 2: Pattern-based corrections.
    /// - Error code normalization (E1023 → E-1023)
    /// - Unit standardization (100um → 100 μm)
    /// - Korean typo fixes
    /// </summary>
    internal string ApplyPatternCorrections(string query, List<CorrectionDetail> corrections)
    {
        var corrected = query;

        // Korean typo corrections
        foreach (var (typo, fix) in KoreanTypoMap)
        {
            if (corrected.Contains(typo, StringComparison.OrdinalIgnoreCase))
            {
                corrected = corrected.Replace(typo, fix, StringComparison.OrdinalIgnoreCase);
                corrections.Add(new CorrectionDetail(2, "KoreanTypo", typo, fix));
            }
        }

        // Error code normalization
        corrected = ErrorCodeRaw.Replace(corrected, match =>
        {
            var prefix = match.Groups[1].Value;
            var number = match.Groups[2].Value;
            var normalized = $"{prefix}-{number}";

            // Only correct if the original didn't have a dash
            if (!query.Contains($"{prefix}-{number}", StringComparison.OrdinalIgnoreCase))
            {
                corrections.Add(new CorrectionDetail(2, "ErrorCode", match.Value, normalized));
            }
            return normalized;
        });

        // Unit normalization
        corrected = UnitPattern.Replace(corrected, match =>
        {
            var value = match.Groups[1].Value;
            var unit = match.Groups[2].Value;

            if (UnitNormMap.TryGetValue(unit, out var normalizedUnit) &&
                !string.Equals(unit, normalizedUnit, StringComparison.Ordinal))
            {
                var normalized = $"{value} {normalizedUnit}";
                corrections.Add(new CorrectionDetail(2, "Unit", match.Value, normalized));
                return normalized;
            }
            return match.Value;
        });

        return corrected;
    }

    /// <summary>
    /// Stage 3: LLM-based correction for complex grammatical/semantic issues.
    /// Only triggered when 30%+ of tokens remain potentially uncorrected.
    /// Returns both original and corrected as search candidates (overcorrection prevention).
    /// </summary>
    internal async Task<string> ApplyLlmCorrectionAsync(
        string originalQuery, string partiallyFixed, List<CorrectionDetail> corrections, CancellationToken ct)
    {
        try
        {
            var systemPrompt = @"당신은 반도체 장비 질문 교정 전문가입니다.
사용자의 질문에서 오타, 문법 오류, 불완전한 문장을 교정해 주세요.
규칙:
1. 반도체/CMP 장비 도메인 용어는 정확한 기술 용어로 교정
2. 동사가 누락된 경우 적절한 동사 추가
3. 주어가 불명확한 경우 문맥에서 유추
4. 원래 의미를 변경하지 않도록 주의
5. 교정된 질문만 출력하고 설명은 하지 마세요.";

            var messages = new List<LlmChatMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = partiallyFixed }
            };

            var llmCorrected = await _llmClient.CompleteChatAsync(
                messages,
                new LlmOptions { Temperature = 0.1f, MaxTokens = 256 },
                ct);

            llmCorrected = llmCorrected.Trim();

            if (!string.IsNullOrEmpty(llmCorrected) &&
                !string.Equals(llmCorrected, partiallyFixed, StringComparison.Ordinal))
            {
                corrections.Add(new CorrectionDetail(3, "LLM", partiallyFixed, llmCorrected));
                return llmCorrected;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM correction (Stage 3) failed, using Stage 1+2 result");
        }

        return partiallyFixed;
    }

    /// <summary>
    /// Records user's acceptance/rejection of a correction for auto-mode tracking.
    /// </summary>
    public void RecordCorrectionFeedback(bool accepted)
    {
        _totalCorrections++;
        if (accepted) _acceptedCorrections++;
    }

    /// <summary>
    /// Returns true if auto-correction mode should be suggested
    /// (90%+ acceptance rate in recent 20+ corrections).
    /// </summary>
    public bool ShouldSuggestAutoMode =>
        _totalCorrections >= 20 && (float)_acceptedCorrections / _totalCorrections >= 0.9f;

    /// <summary>
    /// Computes ratio of tokens that were NOT changed between original and corrected.
    /// High ratio = many tokens unchanged = possibly more to correct via LLM.
    /// </summary>
    private static float ComputeUncorrectedRatio(string original, string corrected)
    {
        if (string.Equals(original, corrected, StringComparison.Ordinal))
            return 1.0f; // Nothing changed at all

        var originalTokens = original.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var correctedTokens = corrected.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (originalTokens.Length == 0) return 0f;

        // Count how many original tokens are still present in corrected
        var unchanged = originalTokens.Count(ot =>
            correctedTokens.Any(ct => string.Equals(ot, ct, StringComparison.OrdinalIgnoreCase)));

        return (float)unchanged / originalTokens.Length;
    }
}

/// <summary>
/// Result of the query intelligence pipeline.
/// </summary>
public sealed record QueryCorrectionResult(
    string OriginalQuery,
    string CorrectedQuery,
    List<CorrectionDetail> Corrections,
    bool WasModified)
{
    /// <summary>
    /// Returns both queries for search (overcorrection prevention).
    /// If modified, searches with both original and corrected.
    /// </summary>
    public List<string> GetSearchQueries()
    {
        if (!WasModified) return [OriginalQuery];
        return [CorrectedQuery, OriginalQuery];
    }
}

/// <summary>
/// Details about a single correction applied.
/// </summary>
public sealed record CorrectionDetail(int Stage, string Type, string Original, string Corrected);
