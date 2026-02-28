using System.Text.Json;
using FabCopilot.RagService.Services.Bm25;
using Microsoft.Extensions.Logging;

namespace FabCopilot.RagService.Services.Evaluation;

/// <summary>
/// RAGAS-inspired RAG evaluation service. Runs ground truth queries through
/// the BM25 index (offline, no external services required) and computes
/// retrieval quality metrics: Recall@K, MRR@K, NDCG@K, Precision@K, MAP@K.
///
/// For full pipeline evaluation (vector + hybrid), use the EvaluateHybridAsync
/// overload which requires an IEmbeddingClient.
/// </summary>
public sealed class RagEvaluationService
{
    private readonly ILogger _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public RagEvaluationService(ILogger<RagEvaluationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Loads ground truth dataset from a JSON file.
    /// </summary>
    public static GroundTruthDataset LoadGroundTruth(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);
        return JsonSerializer.Deserialize<GroundTruthDataset>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to parse ground truth file: {jsonPath}");
    }

    /// <summary>
    /// Runs evaluation against BM25 index using knowledge docs.
    /// This is the offline evaluation mode — no external services required.
    /// </summary>
    public EvaluationReport EvaluateBm25(
        GroundTruthDataset dataset,
        string knowledgeDocsPath,
        int k = 10,
        EvaluationThresholds? thresholds = null)
    {
        // Build BM25 index from knowledge docs
        var bm25 = new Bm25Index(k1: 1.2, b: 0.75);
        var chunkToDoc = new Dictionary<string, string>();

        foreach (var file in Directory.GetFiles(knowledgeDocsPath, "*.md"))
        {
            var text = File.ReadAllText(file);
            var fileName = Path.GetFileName(file);
            var chunks = DocumentIngestor.ChunkMarkdown(text, 512, 128);

            for (var i = 0; i < chunks.Count; i++)
            {
                var chunkId = $"{fileName}:chunk:{i}";
                bm25.AddDocument(chunkId, chunks[i]);
                chunkToDoc[chunkId] = fileName;
            }
        }

        _logger.LogInformation("BM25 index built: {DocCount} chunks from {FolderPath}",
            bm25.DocumentCount, knowledgeDocsPath);

        // Run evaluation
        return EvaluateWithSearchFunc(
            dataset, k, thresholds ?? new EvaluationThresholds(), "BM25",
            (query, topK) =>
            {
                var results = bm25.Search(query, topK);
                return results
                    .Select(r => chunkToDoc.GetValueOrDefault(r.DocumentId, ""))
                    .Where(d => !string.IsNullOrEmpty(d))
                    .Distinct()
                    .ToList();
            },
            (query, topK) =>
            {
                var results = bm25.Search(query, topK);
                return results
                    .SelectMany(r =>
                    {
                        var parts = r.DocumentId.Split(":chunk:");
                        if (parts.Length != 2 || !int.TryParse(parts[1], out var idx))
                            return Enumerable.Empty<string>();

                        var filePath = Path.Combine(knowledgeDocsPath, parts[0]);
                        if (!File.Exists(filePath)) return Enumerable.Empty<string>();

                        var chunks = DocumentIngestor.ChunkMarkdown(File.ReadAllText(filePath), 512, 128);
                        return idx < chunks.Count ? new[] { chunks[idx] } : Enumerable.Empty<string>();
                    })
                    .ToList();
            });
    }

    /// <summary>
    /// Core evaluation logic: runs each query through a search function and computes metrics.
    /// </summary>
    internal EvaluationReport EvaluateWithSearchFunc(
        GroundTruthDataset dataset,
        int k,
        EvaluationThresholds thresholds,
        string pipelineMode,
        Func<string, int, List<string>> searchDocFunc,
        Func<string, int, List<string>> searchChunkTextFunc)
    {
        var report = new EvaluationReport
        {
            PipelineMode = pipelineMode,
            K = k,
            TotalQueries = dataset.Entries.Count,
            Thresholds = thresholds
        };

        var intentGroups = new Dictionary<string, List<(double recall, double mrr, double ndcg)>>();
        var languageGroups = new Dictionary<string, List<(double recall, double mrr, double ndcg)>>();

        double totalRecall = 0, totalMrr = 0, totalNdcg = 0;
        double totalPrecision = 0, totalHit = 0, totalMap = 0;

        foreach (var entry in dataset.Entries)
        {
            // Search with over-fetch (3x K) to get document-level results
            var retrievedDocs = searchDocFunc(entry.Query, k * 3);

            var relevantSet = entry.ExpectedDocs.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var recall = RagEvaluationMetrics.RecallAtK(retrievedDocs, relevantSet, k);
            var mrr = RagEvaluationMetrics.MrrAtK(retrievedDocs, relevantSet, k);
            var ndcg = RagEvaluationMetrics.NdcgAtK(retrievedDocs, relevantSet, k);
            var precision = RagEvaluationMetrics.PrecisionAtK(retrievedDocs, relevantSet, k);
            var hit = RagEvaluationMetrics.HitAtK(retrievedDocs, relevantSet, k);
            var ap = RagEvaluationMetrics.AveragePrecision(retrievedDocs, relevantSet, k);

            totalRecall += recall;
            totalMrr += mrr;
            totalNdcg += ndcg;
            totalPrecision += precision;
            totalHit += hit;
            totalMap += ap;

            // Keyword coverage: check if expected keywords appear in retrieved chunks
            var keywordCoverage = 0.0;
            if (entry.ExpectedKeywords.Count > 0)
            {
                var chunkTexts = searchChunkTextFunc(entry.Query, k);
                var allText = string.Join(" ", chunkTexts);
                var found = entry.ExpectedKeywords.Count(kw =>
                    allText.Contains(kw, StringComparison.OrdinalIgnoreCase));
                keywordCoverage = (double)found / entry.ExpectedKeywords.Count;
            }

            // Per-query result
            report.QueryResults.Add(new QueryEvaluationResult
            {
                Id = entry.Id,
                Query = entry.Query,
                Intent = entry.Intent,
                Language = entry.Language,
                ExpectedDocs = entry.ExpectedDocs,
                RetrievedDocs = retrievedDocs.Take(k).ToList(),
                Recall = recall,
                Mrr = mrr,
                Ndcg = ndcg,
                Hit = hit > 0,
                KeywordCoverage = keywordCoverage
            });

            // Group by intent
            if (!intentGroups.ContainsKey(entry.Intent))
                intentGroups[entry.Intent] = [];
            intentGroups[entry.Intent].Add((recall, mrr, ndcg));

            // Group by language
            if (!languageGroups.ContainsKey(entry.Language))
                languageGroups[entry.Language] = [];
            languageGroups[entry.Language].Add((recall, mrr, ndcg));
        }

        var n = dataset.Entries.Count;
        if (n > 0)
        {
            report.RecallAtK = totalRecall / n;
            report.MrrAtK = totalMrr / n;
            report.NdcgAtK = totalNdcg / n;
            report.PrecisionAtK = totalPrecision / n;
            report.HitRateAtK = totalHit / n;
            report.MapAtK = totalMap / n;
        }

        // Intent breakdown
        foreach (var (intent, metrics) in intentGroups)
        {
            report.ByIntent[intent] = new IntentMetrics
            {
                Count = metrics.Count,
                RecallAtK = metrics.Average(m => m.recall),
                MrrAtK = metrics.Average(m => m.mrr),
                NdcgAtK = metrics.Average(m => m.ndcg)
            };
        }

        // Language breakdown
        foreach (var (lang, metrics) in languageGroups)
        {
            report.ByLanguage[lang] = new LanguageMetrics
            {
                Count = metrics.Count,
                RecallAtK = metrics.Average(m => m.recall),
                MrrAtK = metrics.Average(m => m.mrr),
                NdcgAtK = metrics.Average(m => m.ndcg)
            };
        }

        _logger.LogInformation(
            "RAG Evaluation [{Pipeline}] — Recall@{K}: {Recall:P1}, MRR@{K}: {Mrr:P1}, NDCG@{K}: {Ndcg:P1}, Passed: {Passed}",
            pipelineMode, k, report.RecallAtK, k, report.MrrAtK, k, report.NdcgAtK, report.Passed);

        return report;
    }

    /// <summary>
    /// Serializes an evaluation report to JSON.
    /// </summary>
    public static string ToJson(EvaluationReport report)
    {
        return JsonSerializer.Serialize(report, JsonOptions);
    }

    /// <summary>
    /// Saves an evaluation report to a JSON file.
    /// </summary>
    public static void SaveReport(EvaluationReport report, string outputPath)
    {
        File.WriteAllText(outputPath, ToJson(report));
    }

    /// <summary>
    /// Generates a human-readable summary of the evaluation results.
    /// </summary>
    public static string FormatSummary(EvaluationReport report)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"═══════════════════════════════════════════════════════");
        sb.AppendLine($"  RAG Evaluation Report — {report.PipelineMode}");
        sb.AppendLine($"  {report.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"═══════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine($"  Total Queries:  {report.TotalQueries}");
        sb.AppendLine($"  K:              {report.K}");
        sb.AppendLine();
        sb.AppendLine($"  ── Aggregate Metrics ─────────────────────────────");
        sb.AppendLine($"  Recall@{report.K}:     {report.RecallAtK:P1}  (threshold: {report.Thresholds.MinRecallAtK:P0})");
        sb.AppendLine($"  MRR@{report.K}:        {report.MrrAtK:P1}  (threshold: {report.Thresholds.MinMrrAtK:P0})");
        sb.AppendLine($"  NDCG@{report.K}:       {report.NdcgAtK:P1}  (threshold: {report.Thresholds.MinNdcgAtK:P0})");
        sb.AppendLine($"  Precision@{report.K}:  {report.PrecisionAtK:P1}");
        sb.AppendLine($"  Hit Rate@{report.K}:   {report.HitRateAtK:P1}");
        sb.AppendLine($"  MAP@{report.K}:        {report.MapAtK:P1}");
        sb.AppendLine();
        sb.AppendLine($"  Result:         {(report.Passed ? "✅ PASS" : "❌ FAIL")}");

        if (report.ByIntent.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"  ── By Intent ─────────────────────────────────────");
            foreach (var (intent, m) in report.ByIntent.OrderByDescending(kv => kv.Value.Count))
            {
                sb.AppendLine($"  {intent,-12} (n={m.Count,3})  Recall={m.RecallAtK:P1}  MRR={m.MrrAtK:P1}  NDCG={m.NdcgAtK:P1}");
            }
        }

        if (report.ByLanguage.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"  ── By Language ────────────────────────────────────");
            foreach (var (lang, m) in report.ByLanguage.OrderByDescending(kv => kv.Value.Count))
            {
                sb.AppendLine($"  {lang,-5} (n={m.Count,3})  Recall={m.RecallAtK:P1}  MRR={m.MrrAtK:P1}  NDCG={m.NdcgAtK:P1}");
            }
        }

        // Failed queries
        var failed = report.QueryResults.Where(q => !q.Hit).ToList();
        if (failed.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"  ── Failed Queries ({failed.Count}) ──────────────────────");
            foreach (var q in failed.Take(20))
            {
                sb.AppendLine($"  [{q.Id}] {q.Query}");
                sb.AppendLine($"    Expected: {string.Join(", ", q.ExpectedDocs)}");
                sb.AppendLine($"    Got:      {string.Join(", ", q.RetrievedDocs.Take(3))}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"═══════════════════════════════════════════════════════");
        return sb.ToString();
    }
}
