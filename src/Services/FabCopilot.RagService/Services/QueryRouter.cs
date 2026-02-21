using System.Text.RegularExpressions;
using FabCopilot.Contracts.Enums;

namespace FabCopilot.RagService.Services;

/// <summary>
/// Classifies user queries into one of 7 intents using rule-based keyword matching.
/// Falls back to General for unrecognized patterns.
/// </summary>
public static partial class QueryRouter
{
    /// <summary>
    /// Rule definitions: pattern → intent, ordered by priority.
    /// </summary>
    private static readonly (Regex Pattern, QueryIntent Intent)[] Rules =
    [
        // Error: alarm codes, error keywords
        (ErrorPattern(), QueryIntent.Error),
        // Procedure: process/action keywords
        (ProcedurePattern(), QueryIntent.Procedure),
        // Part: component/consumable keywords
        (PartPattern(), QueryIntent.Part),
        // Definition: what-is keywords
        (DefinitionPattern(), QueryIntent.Definition),
        // Spec: specification/parameter keywords
        (SpecPattern(), QueryIntent.Spec),
        // Comparison: comparative keywords
        (ComparisonPattern(), QueryIntent.Comparison),
    ];

    /// <summary>
    /// Classifies a query into an intent using rule-based matching.
    /// </summary>
    public static QueryIntent Classify(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return QueryIntent.General;

        foreach (var (pattern, intent) in Rules)
        {
            if (pattern.IsMatch(query))
                return intent;
        }

        return QueryIntent.General;
    }

    /// <summary>
    /// Returns recommended doc_type filter values for a given intent.
    /// Used to boost or filter Qdrant results by document type.
    /// </summary>
    public static string[]? GetPreferredDocTypes(QueryIntent intent)
    {
        return intent switch
        {
            QueryIntent.Error => ["alarm", "troubleshooting", "error"],
            QueryIntent.Procedure => ["procedure", "troubleshooting", "maintenance"],
            QueryIntent.Part => ["maintenance", "parts", "consumable"],
            QueryIntent.Definition => ["overview", "glossary", "definition"],
            QueryIntent.Spec => ["specification", "parameter", "spec"],
            QueryIntent.Comparison => null, // cross-document, no filter
            QueryIntent.General => null,
            _ => null
        };
    }

    // ─── Regex patterns ─────────────────────────────────────────────────

    [GeneratedRegex(@"(알람|에러|오류|alarm|error|fault|A[0-9]{2,3}|E[0-9]{2,3}|경고|warning)", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorPattern();

    [GeneratedRegex(@"(절차|방법|교체|조치|순서|단계|설치|분해|how\s*to|procedure|replace|install|step)", RegexOptions.IgnoreCase)]
    private static partial Regex ProcedurePattern();

    [GeneratedRegex(@"(부품|소모품|수명|교환|파트|컨디셔너|패드|리테이너|링|part|consumable|lifetime|lifespan)", RegexOptions.IgnoreCase)]
    private static partial Regex PartPattern();

    [GeneratedRegex(@"(정의|무엇|뜻|의미|개념|what\s+is|define|definition|meaning)", RegexOptions.IgnoreCase)]
    private static partial Regex DefinitionPattern();

    [GeneratedRegex(@"(사양|규격|범위|스펙|파라미터|설정값|spec|specification|parameter|range|threshold)", RegexOptions.IgnoreCase)]
    private static partial Regex SpecPattern();

    [GeneratedRegex(@"(비교|차이|다른점|대비|versus|vs\.?|compare|comparison|differ)", RegexOptions.IgnoreCase)]
    private static partial Regex ComparisonPattern();
}
