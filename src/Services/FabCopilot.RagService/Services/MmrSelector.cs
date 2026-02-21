using FabCopilot.VectorStore.Models;

namespace FabCopilot.RagService.Services;

/// <summary>
/// Maximal Marginal Relevance (MMR) selector for diversifying retrieval results.
/// Uses TF-IDF-like term overlap to compute inter-chunk similarity without needing embeddings.
/// MMR(d) = lambda * Sim(d, query) - (1 - lambda) * max(Sim(d, d_i))
/// </summary>
public static class MmrSelector
{
    /// <summary>
    /// Selects top-K results from candidates using MMR to balance relevance and diversity.
    /// </summary>
    /// <param name="candidates">Ranked candidates (already re-ranked by relevance score).</param>
    /// <param name="query">The original user query text.</param>
    /// <param name="topK">Number of results to select.</param>
    /// <param name="lambda">Balance between relevance (1.0) and diversity (0.0). Default 0.7.</param>
    /// <returns>Selected results with maximal marginal relevance.</returns>
    public static List<VectorSearchResult> Select(
        IReadOnlyList<VectorSearchResult> candidates,
        string query,
        int topK,
        double lambda = 0.7)
    {
        if (candidates.Count <= topK)
            return candidates.ToList();

        // Build TF vectors for all candidates and the query
        var queryTerms = Tokenize(query);
        var queryTf = BuildTermFrequency(queryTerms);

        var candidateTfs = candidates
            .Select(c =>
            {
                var text = c.Payload.TryGetValue("text", out var t) ? t.ToString() ?? "" : "";
                return BuildTermFrequency(Tokenize(text));
            })
            .ToList();

        // Compute IDF from all candidate documents
        var idf = ComputeIdf(candidateTfs);

        // Pre-compute query-to-candidate relevance scores (normalized to [0,1])
        var queryRelevance = new double[candidates.Count];
        var maxScore = candidates.Max(c => c.Score);
        var minScore = candidates.Min(c => c.Score);
        var scoreRange = maxScore - minScore;

        for (var i = 0; i < candidates.Count; i++)
        {
            // Combine original retrieval score (normalized) with text similarity to query
            var normalizedScore = scoreRange > 0
                ? (candidates[i].Score - minScore) / scoreRange
                : 1.0;
            var textSim = CosineSimilarity(queryTf, candidateTfs[i], idf);
            queryRelevance[i] = 0.5 * normalizedScore + 0.5 * textSim;
        }

        // Greedy MMR selection
        var selected = new List<int>();
        var remaining = Enumerable.Range(0, candidates.Count).ToHashSet();

        // Start with the most relevant candidate
        var first = remaining.MaxBy(i => queryRelevance[i]);
        selected.Add(first);
        remaining.Remove(first);

        while (selected.Count < topK && remaining.Count > 0)
        {
            var bestIdx = -1;
            var bestMmr = double.MinValue;

            foreach (var i in remaining)
            {
                // Max similarity to any already-selected document
                var maxSimToSelected = selected.Max(j =>
                    CosineSimilarity(candidateTfs[i], candidateTfs[j], idf));

                var mmr = lambda * queryRelevance[i] - (1.0 - lambda) * maxSimToSelected;

                if (mmr > bestMmr)
                {
                    bestMmr = mmr;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0) break;

            selected.Add(bestIdx);
            remaining.Remove(bestIdx);
        }

        return selected.Select(i => candidates[i]).ToList();
    }

    // ─── TF-IDF helpers ─────────────────────────────────────────────────

    internal static List<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return text.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', '!', '?', ':', ';', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\' },
                StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2)
            .ToList();
    }

    internal static Dictionary<string, int> BuildTermFrequency(List<string> tokens)
    {
        var tf = new Dictionary<string, int>();
        foreach (var token in tokens)
        {
            tf[token] = tf.TryGetValue(token, out var c) ? c + 1 : 1;
        }
        return tf;
    }

    internal static Dictionary<string, double> ComputeIdf(List<Dictionary<string, int>> documentTfs)
    {
        var n = documentTfs.Count;
        if (n == 0) return new Dictionary<string, double>();

        var df = new Dictionary<string, int>();
        foreach (var docTf in documentTfs)
        {
            foreach (var term in docTf.Keys)
            {
                df[term] = df.TryGetValue(term, out var c) ? c + 1 : 1;
            }
        }

        var idf = new Dictionary<string, double>();
        foreach (var (term, count) in df)
        {
            idf[term] = Math.Log((double)(n + 1) / (count + 1)) + 1.0;
        }
        return idf;
    }

    internal static double CosineSimilarity(
        Dictionary<string, int> tf1,
        Dictionary<string, int> tf2,
        Dictionary<string, double> idf)
    {
        if (tf1.Count == 0 || tf2.Count == 0)
            return 0.0;

        var dotProduct = 0.0;
        var norm1 = 0.0;
        var norm2 = 0.0;

        // All unique terms
        var allTerms = new HashSet<string>(tf1.Keys);
        allTerms.UnionWith(tf2.Keys);

        foreach (var term in allTerms)
        {
            var idfVal = idf.TryGetValue(term, out var v) ? v : 1.0;
            var w1 = (tf1.TryGetValue(term, out var c1) ? c1 : 0) * idfVal;
            var w2 = (tf2.TryGetValue(term, out var c2) ? c2 : 0) * idfVal;

            dotProduct += w1 * w2;
            norm1 += w1 * w1;
            norm2 += w2 * w2;
        }

        var denominator = Math.Sqrt(norm1) * Math.Sqrt(norm2);
        return denominator > 0 ? dotProduct / denominator : 0.0;
    }
}
