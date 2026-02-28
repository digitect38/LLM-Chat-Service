using System.Text.RegularExpressions;
using FabCopilot.Contracts.Models;

namespace FabCopilot.McpLogServer.Analysis;

/// <summary>
/// Extracts causal knowledge triples (error → cause → action) from technical documents.
/// Uses regex-based pattern matching optimized for semiconductor equipment manuals.
/// </summary>
public static partial class CausalKnowledgeExtractor
{
    // ── Error Code Patterns ──────────────────────────────────────────

    [GeneratedRegex(@"(?:에러\s*코드|error\s*code|alarm\s*code|알람\s*코드|fault\s*code)\s*[:\-]?\s*([A-Z][\-]?\d{2,5}(?:\.\d+)?)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ErrorCodePattern();

    [GeneratedRegex(@"\b([A-Z]\-?\d{3,5})\b")]
    private static partial Regex StandaloneErrorCodePattern();

    // ── Cause-Action Patterns ────────────────────────────────────────

    // Korean: "원인: ..." or "원인 - ..."
    [GeneratedRegex(@"(?:원인|원인\s*분석|root\s*cause|cause|진단)\s*[:\-]\s*(.+?)(?:\n|$)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex CausePatternKo();

    // Korean: "조치: ..." or "조치 방법 - ..."
    [GeneratedRegex(@"(?:조치|조치\s*방법|대처|해결|corrective\s*action|action|resolution|remedy)\s*[:\-]\s*(.+?)(?:\n|$)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ActionPatternKo();

    // Korean: "증상: ..." or "현상 - ..."
    [GeneratedRegex(@"(?:증상|현상|symptom|phenomenon|indication)\s*[:\-]\s*(.+?)(?:\n|$)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex SymptomPattern();

    // Table-style: "| E-1023 | description | cause | action |"
    [GeneratedRegex(@"^\|\s*([A-Z]\-?\d{2,5})\s*\|\s*(.+?)\s*\|\s*(.+?)\s*\|\s*(.+?)\s*\|",
        RegexOptions.Multiline)]
    private static partial Regex TableRowPattern();

    // "If ... then ..." or "...인 경우 ...하십시오"
    [GeneratedRegex(@"(?:if|when|인\s*경우|발생\s*시)\s+(.+?)\s*[,，]\s*(?:then|check|replace|교체|확인|점검)\s+(.+?)(?:\.|。|\n|$)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ConditionalPattern();

    // ── RUL / Lifespan Patterns ──────────────────────────────────────

    // "수명: 500시간" or "expected life: 500 hours"
    [GeneratedRegex(@"(?:수명|교체\s*주기|expected\s*life|service\s*life|lifetime|사용\s*한도)\s*[:\-]?\s*(?:약\s*)?(\d[\d,\.]*)\s*(시간|hours?|hrs?|일|days?|개월|months?|wafers?|매)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex LifespanPattern();

    // "N wafers" or "N매 가공 후"
    [GeneratedRegex(@"(\d[\d,]*)\s*(?:wafers?|매)\s*(?:가공|processing|후|after)?",
        RegexOptions.IgnoreCase)]
    private static partial Regex WaferLifePattern();

    // Component name before lifespan: "패드 수명: ..." or "Pad life: ..."
    [GeneratedRegex(@"([\w가-힣]+(?:\s+[\w가-힣]+)?)\s*(?:의\s*)?(?:수명|교체\s*주기|expected\s*life|service\s*life|lifetime)\s*[:\-]",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ComponentLifespanPattern();

    // ── Extraction Methods ───────────────────────────────────────────

    /// <summary>
    /// Extracts causal knowledge entries from a document chunk.
    /// </summary>
    public static List<CausalKnowledgeEntry> ExtractFromChunk(
        string text, string documentId, string? section = null, string? equipmentType = null)
    {
        var entries = new List<CausalKnowledgeEntry>();

        // Strategy 1: Table-based extraction (highest confidence)
        entries.AddRange(ExtractFromTable(text, documentId, section, equipmentType));

        // Strategy 2: Structured cause-action blocks
        entries.AddRange(ExtractFromStructuredBlocks(text, documentId, section, equipmentType));

        // Strategy 3: Conditional patterns (if/when → action)
        entries.AddRange(ExtractFromConditionals(text, documentId, section, equipmentType));

        // Deduplicate by error code + cause
        return DeduplicateEntries(entries);
    }

    /// <summary>
    /// Extracts cold-start RUL estimates from document text.
    /// </summary>
    public static List<ColdStartRulEstimate> ExtractRulEstimates(
        string text, string documentId, string? section = null, string? equipmentType = null)
    {
        var estimates = new List<ColdStartRulEstimate>();

        var lifespanMatches = LifespanPattern().Matches(text);
        foreach (Match match in lifespanMatches)
        {
            var valueStr = match.Groups[1].Value.Replace(",", "");
            if (!double.TryParse(valueStr, out var value)) continue;

            var unit = match.Groups[2].Value.ToLowerInvariant();

            // Find the component name near this match
            var contextStart = Math.Max(0, match.Index - 200);
            var contextLen = Math.Min(200, match.Index - contextStart);
            var precedingText = text.Substring(contextStart, contextLen + match.Length);
            var componentMatch = ComponentLifespanPattern().Match(precedingText);
            var componentName = componentMatch.Success
                ? componentMatch.Groups[1].Value.Trim()
                : "Unknown Component";

            var estimate = new ColdStartRulEstimate
            {
                ComponentName = componentName,
                SourceDocument = documentId,
                SourceSection = section,
                EquipmentType = equipmentType
            };

            // Convert to hours
            estimate.ExpectedLifeHours = unit switch
            {
                "시간" or "hours" or "hour" or "hrs" or "hr" => value,
                "일" or "days" or "day" => value * 24,
                "개월" or "months" or "month" => value * 24 * 30,
                _ => value
            };

            // Check for wafer-based life
            var waferMatch = WaferLifePattern().Match(text[match.Index..Math.Min(text.Length, match.Index + 100)]);
            if (waferMatch.Success && int.TryParse(waferMatch.Groups[1].Value.Replace(",", ""), out var waferCount))
            {
                estimate.ExpectedLifeWafers = waferCount;
            }

            // Check if unit was wafer
            if (unit is "wafers" or "wafer" or "매")
            {
                estimate.ExpectedLifeWafers = (int)value;
                estimate.ExpectedLifeHours = 0; // No hour-based estimate
            }

            estimates.Add(estimate);
        }

        return estimates;
    }

    // ── Private Helpers ──────────────────────────────────────────────

    private static List<CausalKnowledgeEntry> ExtractFromTable(
        string text, string documentId, string? section, string? equipmentType)
    {
        var entries = new List<CausalKnowledgeEntry>();
        var matches = TableRowPattern().Matches(text);

        foreach (Match match in matches)
        {
            var errorCode = match.Groups[1].Value.Trim();
            var symptom = match.Groups[2].Value.Trim();
            var cause = match.Groups[3].Value.Trim();
            var action = match.Groups[4].Value.Trim();

            if (string.IsNullOrWhiteSpace(cause) || cause == "-" || cause == "—")
                continue;

            entries.Add(new CausalKnowledgeEntry
            {
                Id = $"causal:{documentId}:{errorCode}",
                ErrorCode = errorCode,
                Symptom = symptom,
                Cause = cause,
                Action = action,
                SourceDocument = documentId,
                SourceSection = section,
                EquipmentType = equipmentType,
                Confidence = 0.9 // High confidence for structured table data
            });
        }

        return entries;
    }

    private static List<CausalKnowledgeEntry> ExtractFromStructuredBlocks(
        string text, string documentId, string? section, string? equipmentType)
    {
        var entries = new List<CausalKnowledgeEntry>();

        // Find error codes in text
        var errorCodes = new List<(string Code, int Position)>();
        foreach (Match m in ErrorCodePattern().Matches(text))
            errorCodes.Add((m.Groups[1].Value, m.Index));
        foreach (Match m in StandaloneErrorCodePattern().Matches(text))
        {
            if (!errorCodes.Any(e => e.Code == m.Groups[1].Value))
                errorCodes.Add((m.Groups[1].Value, m.Index));
        }

        // Find symptoms, causes, actions
        var symptoms = SymptomPattern().Matches(text);
        var causes = CausePatternKo().Matches(text);
        var actions = ActionPatternKo().Matches(text);

        if (causes.Count == 0 && actions.Count == 0)
            return entries;

        // Match causes and actions to nearest error code
        foreach (Match causeMatch in causes)
        {
            var cause = causeMatch.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(cause)) continue;

            // Find nearest error code (within 500 chars before)
            var nearestError = errorCodes
                .Where(e => e.Position < causeMatch.Index && causeMatch.Index - e.Position < 500)
                .OrderByDescending(e => e.Position)
                .FirstOrDefault();

            // Find nearest action (within 300 chars after cause)
            var nearestAction = actions
                .Cast<Match>()
                .Where(a => a.Index > causeMatch.Index && a.Index - causeMatch.Index < 300)
                .FirstOrDefault();

            // Find nearest symptom (within 500 chars before cause)
            var nearestSymptom = symptoms
                .Cast<Match>()
                .Where(s => s.Index < causeMatch.Index && causeMatch.Index - s.Index < 500)
                .OrderByDescending(s => s.Index)
                .FirstOrDefault();

            var entryId = nearestError.Code != null
                ? $"causal:{documentId}:{nearestError.Code}"
                : $"causal:{documentId}:{causeMatch.Index}";

            entries.Add(new CausalKnowledgeEntry
            {
                Id = entryId,
                ErrorCode = nearestError.Code,
                Symptom = nearestSymptom?.Groups[1].Value.Trim() ?? "",
                Cause = cause,
                Action = nearestAction?.Groups[1].Value.Trim() ?? "",
                SourceDocument = documentId,
                SourceSection = section,
                EquipmentType = equipmentType,
                Confidence = nearestError.Code != null ? 0.7 : 0.5
            });
        }

        return entries;
    }

    private static List<CausalKnowledgeEntry> ExtractFromConditionals(
        string text, string documentId, string? section, string? equipmentType)
    {
        var entries = new List<CausalKnowledgeEntry>();
        var matches = ConditionalPattern().Matches(text);

        foreach (Match match in matches)
        {
            var condition = match.Groups[1].Value.Trim();
            var action = match.Groups[2].Value.Trim();

            if (string.IsNullOrWhiteSpace(action)) continue;

            // Check if there's an error code near this conditional
            var contextStart = Math.Max(0, match.Index - 100);
            var context = text.Substring(contextStart, match.Index - contextStart + match.Length);
            var errorMatch = StandaloneErrorCodePattern().Match(context);

            entries.Add(new CausalKnowledgeEntry
            {
                Id = $"causal:{documentId}:cond:{match.Index}",
                ErrorCode = errorMatch.Success ? errorMatch.Groups[1].Value : null,
                Symptom = condition,
                Cause = "", // Conditional patterns don't always have explicit causes
                Action = action,
                SourceDocument = documentId,
                SourceSection = section,
                EquipmentType = equipmentType,
                Confidence = 0.4 // Lower confidence for conditional extraction
            });
        }

        return entries;
    }

    private static List<CausalKnowledgeEntry> DeduplicateEntries(List<CausalKnowledgeEntry> entries)
    {
        var seen = new HashSet<string>();
        var result = new List<CausalKnowledgeEntry>();

        foreach (var entry in entries.OrderByDescending(e => e.Confidence))
        {
            var key = $"{entry.ErrorCode ?? ""}:{entry.Cause}".ToLowerInvariant();
            if (seen.Add(key))
                result.Add(entry);
        }

        return result;
    }
}
