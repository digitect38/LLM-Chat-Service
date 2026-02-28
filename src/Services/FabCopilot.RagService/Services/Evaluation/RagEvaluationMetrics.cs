namespace FabCopilot.RagService.Services.Evaluation;

/// <summary>
/// RAGAS-inspired retrieval quality metrics calculator.
/// Computes Recall@K, MRR@K, NDCG@K for evaluating search pipeline quality.
/// All methods are stateless and operate on ranked result lists.
/// </summary>
public static class RagEvaluationMetrics
{
    /// <summary>
    /// Recall@K: fraction of relevant documents found in top-K results.
    /// Recall@K = |relevant ∩ retrieved@K| / |relevant|
    /// </summary>
    public static double RecallAtK(IReadOnlyList<string> retrievedDocIds, IReadOnlySet<string> relevantDocIds, int k)
    {
        if (relevantDocIds.Count == 0) return 0.0;

        var topK = retrievedDocIds.Take(k).ToHashSet();
        var hits = topK.Count(id => relevantDocIds.Contains(id));
        return (double)hits / relevantDocIds.Count;
    }

    /// <summary>
    /// Precision@K: fraction of top-K results that are relevant.
    /// Precision@K = |relevant ∩ retrieved@K| / K
    /// </summary>
    public static double PrecisionAtK(IReadOnlyList<string> retrievedDocIds, IReadOnlySet<string> relevantDocIds, int k)
    {
        if (k == 0) return 0.0;

        var topK = retrievedDocIds.Take(k).ToList();
        var hits = topK.Count(id => relevantDocIds.Contains(id));
        return (double)hits / k;
    }

    /// <summary>
    /// MRR@K (Mean Reciprocal Rank): 1/rank of the first relevant result within top-K.
    /// Returns 0 if no relevant result is found in top-K.
    /// </summary>
    public static double MrrAtK(IReadOnlyList<string> retrievedDocIds, IReadOnlySet<string> relevantDocIds, int k)
    {
        var topK = retrievedDocIds.Take(k).ToList();
        for (var i = 0; i < topK.Count; i++)
        {
            if (relevantDocIds.Contains(topK[i]))
                return 1.0 / (i + 1);
        }
        return 0.0;
    }

    /// <summary>
    /// NDCG@K (Normalized Discounted Cumulative Gain):
    /// Measures ranking quality by comparing actual DCG against ideal DCG.
    /// Uses binary relevance (1 if relevant, 0 otherwise).
    /// </summary>
    public static double NdcgAtK(IReadOnlyList<string> retrievedDocIds, IReadOnlySet<string> relevantDocIds, int k)
    {
        var topK = retrievedDocIds.Take(k).ToList();

        // DCG: sum of relevance / log2(rank + 1)
        var dcg = 0.0;
        for (var i = 0; i < topK.Count; i++)
        {
            var rel = relevantDocIds.Contains(topK[i]) ? 1.0 : 0.0;
            dcg += rel / Math.Log2(i + 2); // i+2 because rank is 1-indexed
        }

        // Ideal DCG: all relevant docs at top positions
        var idealK = Math.Min(relevantDocIds.Count, k);
        var idcg = 0.0;
        for (var i = 0; i < idealK; i++)
        {
            idcg += 1.0 / Math.Log2(i + 2);
        }

        return idcg == 0 ? 0.0 : dcg / idcg;
    }

    /// <summary>
    /// Hit@K: binary indicator — 1 if any relevant document is in top-K, 0 otherwise.
    /// </summary>
    public static double HitAtK(IReadOnlyList<string> retrievedDocIds, IReadOnlySet<string> relevantDocIds, int k)
    {
        return retrievedDocIds.Take(k).Any(id => relevantDocIds.Contains(id)) ? 1.0 : 0.0;
    }

    /// <summary>
    /// Average Precision (AP): average of Precision@k for each position where a relevant doc appears.
    /// Used to compute MAP (Mean Average Precision) across queries.
    /// </summary>
    public static double AveragePrecision(IReadOnlyList<string> retrievedDocIds, IReadOnlySet<string> relevantDocIds, int k)
    {
        if (relevantDocIds.Count == 0) return 0.0;

        var topK = retrievedDocIds.Take(k).ToList();
        var hits = 0;
        var sumPrecision = 0.0;

        for (var i = 0; i < topK.Count; i++)
        {
            if (relevantDocIds.Contains(topK[i]))
            {
                hits++;
                sumPrecision += (double)hits / (i + 1);
            }
        }

        return sumPrecision / relevantDocIds.Count;
    }
}
