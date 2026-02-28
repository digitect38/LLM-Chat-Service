using System.Text.RegularExpressions;

namespace FabCopilot.RagService.Services.ImageOcr;

/// <summary>
/// Detects and parses figure/diagram cross-references in document text.
/// Handles patterns like "그림 3.2 참조", "Figure 3.2", "Fig. 3",
/// "표 2.1", "Table 4", etc.
/// </summary>
public static partial class FigureCrossReferenceParser
{
    /// <summary>
    /// A detected figure or table reference in the text.
    /// </summary>
    public sealed record FigureReference(
        string ReferenceType,   // "figure", "table", "diagram", "photo"
        string ReferenceId,     // e.g. "3.2", "4", "A-1"
        string FullMatch,       // The complete matched text
        int Position,           // Character position in source text
        string? Caption = null  // Caption text if found nearby
    );

    /// <summary>
    /// Finds all figure/table/diagram references in the given text.
    /// </summary>
    public static List<FigureReference> FindReferences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var results = new List<FigureReference>();

        // Korean figure references: 그림 N.N, 도면 N, 사진 N
        foreach (Match m in KoreanFigurePattern().Matches(text))
        {
            var type = m.Groups["type"].Value switch
            {
                "그림" => "figure",
                "도면" => "diagram",
                "사진" => "photo",
                _ => "figure"
            };
            results.Add(new FigureReference(type, m.Groups["id"].Value.Trim(), m.Value, m.Index));
        }

        // Korean table references: 표 N.N
        foreach (Match m in KoreanTablePattern().Matches(text))
        {
            results.Add(new FigureReference("table", m.Groups["id"].Value.Trim(), m.Value, m.Index));
        }

        // English figure references: Figure N.N, Fig. N, Diagram N
        foreach (Match m in EnglishFigurePattern().Matches(text))
        {
            var type = m.Groups["type"].Value.ToLowerInvariant() switch
            {
                "figure" or "fig" => "figure",
                "diagram" => "diagram",
                "photo" or "photograph" => "photo",
                _ => "figure"
            };
            results.Add(new FigureReference(type, m.Groups["id"].Value.Trim(), m.Value, m.Index));
        }

        // English table references: Table N.N
        foreach (Match m in EnglishTablePattern().Matches(text))
        {
            results.Add(new FigureReference("table", m.Groups["id"].Value.Trim(), m.Value, m.Index));
        }

        // Deduplicate by position
        return results
            .GroupBy(r => r.Position)
            .Select(g => g.First())
            .OrderBy(r => r.Position)
            .ToList();
    }

    /// <summary>
    /// Extracts figure/table captions from text. Captions typically follow
    /// the pattern "그림 N.N: caption text" or "Figure N.N - caption text".
    /// </summary>
    public static List<FigureReference> FindCaptions(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var results = new List<FigureReference>();

        // Korean captions: 그림 3.2: Caption text / 그림 3.2 - Caption text
        foreach (Match m in KoreanCaptionPattern().Matches(text))
        {
            var type = m.Groups["type"].Value switch
            {
                "그림" => "figure",
                "도면" => "diagram",
                "표" => "table",
                "사진" => "photo",
                _ => "figure"
            };
            results.Add(new FigureReference(
                type,
                m.Groups["id"].Value.Trim(),
                m.Value,
                m.Index,
                m.Groups["caption"].Value.Trim()));
        }

        // English captions: Figure 3.2: Caption text / Figure 3.2 - Caption text
        foreach (Match m in EnglishCaptionPattern().Matches(text))
        {
            var type = m.Groups["type"].Value.ToLowerInvariant() switch
            {
                "figure" or "fig" => "figure",
                "table" => "table",
                "diagram" => "diagram",
                "photo" => "photo",
                _ => "figure"
            };
            results.Add(new FigureReference(
                type,
                m.Groups["id"].Value.Trim(),
                m.Value,
                m.Index,
                m.Groups["caption"].Value.Trim()));
        }

        return results
            .GroupBy(r => r.Position)
            .Select(g => g.First())
            .OrderBy(r => r.Position)
            .ToList();
    }

    /// <summary>
    /// Enriches chunk text with figure metadata by appending caption information
    /// for any figure references found in the chunk.
    /// </summary>
    public static string EnrichChunkWithFigureContext(
        string chunkText,
        Dictionary<string, string> figureCaptionMap)
    {
        if (figureCaptionMap.Count == 0) return chunkText;

        var references = FindReferences(chunkText);
        if (references.Count == 0) return chunkText;

        var additions = new List<string>();
        foreach (var reference in references)
        {
            var key = $"{reference.ReferenceType}:{reference.ReferenceId}";
            if (figureCaptionMap.TryGetValue(key, out var caption))
            {
                additions.Add($"[{reference.ReferenceType.ToUpperInvariant()} {reference.ReferenceId}: {caption}]");
            }
        }

        if (additions.Count == 0) return chunkText;

        return chunkText + "\n\n" + string.Join("\n", additions);
    }

    /// <summary>
    /// Builds a figure caption map from an entire document by scanning for caption definitions.
    /// Key: "figure:3.2", Value: "Caption text"
    /// </summary>
    public static Dictionary<string, string> BuildCaptionMap(string documentText)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var captions = FindCaptions(documentText);

        foreach (var caption in captions)
        {
            var key = $"{caption.ReferenceType}:{caption.ReferenceId}";
            if (!string.IsNullOrWhiteSpace(caption.Caption) && !map.ContainsKey(key))
            {
                map[key] = caption.Caption;
            }
        }

        return map;
    }

    // ── Regex Patterns ───────────────────────────────────────────────

    [GeneratedRegex(@"(?<type>그림|도면|사진)\s*(?<id>[\d]+(?:[.\-][\d]+)*)", RegexOptions.IgnoreCase)]
    private static partial Regex KoreanFigurePattern();

    [GeneratedRegex(@"표\s*(?<id>[\d]+(?:[.\-][\d]+)*)", RegexOptions.IgnoreCase)]
    private static partial Regex KoreanTablePattern();

    [GeneratedRegex(@"(?<type>Figure|Fig|Diagram|Photo|Photograph)\.?\s*(?<id>[\d]+(?:[.\-][\d]+)*)", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishFigurePattern();

    [GeneratedRegex(@"Table\s*(?<id>[\d]+(?:[.\-][\d]+)*)", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishTablePattern();

    [GeneratedRegex(@"(?<type>그림|도면|표|사진)\s*(?<id>[\d]+(?:[.\-][\d]+)*)\s*[:\-–—]\s*(?<caption>[^\n]{3,80})", RegexOptions.IgnoreCase)]
    private static partial Regex KoreanCaptionPattern();

    [GeneratedRegex(@"(?<type>Figure|Fig|Table|Diagram|Photo)\.?\s*(?<id>[\d]+(?:[.\-][\d]+)*)\s*[:\-–—]\s*(?<caption>[^\n]{3,80})", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishCaptionPattern();
}
