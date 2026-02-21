using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace FabCopilot.RagService.Services.Bm25;

/// <summary>
/// Thread-safe in-process BM25 (Okapi BM25) inverted index.
/// Uses whitespace + Korean bigram tokenization for mixed Korean/English text.
/// </summary>
public sealed partial class Bm25Index : IBm25Index
{
    private readonly double _k1;
    private readonly double _b;
    private readonly object _lock = new();

    // documentId -> (tokens, document length)
    private readonly Dictionary<string, (List<string> Tokens, int Length)> _documents = new();

    // term -> set of documentIds containing the term
    private readonly Dictionary<string, HashSet<string>> _invertedIndex = new();

    // term -> documentId -> term frequency count
    private readonly Dictionary<string, Dictionary<string, int>> _termFrequencies = new();

    private double _averageDocLength;

    public Bm25Index(double k1 = 1.2, double b = 0.75)
    {
        _k1 = k1;
        _b = b;
    }

    public int DocumentCount
    {
        get { lock (_lock) return _documents.Count; }
    }

    public void AddDocument(string documentId, string text)
    {
        var tokens = Tokenize(text);

        lock (_lock)
        {
            // Remove existing if present
            RemoveDocumentInternal(documentId);

            _documents[documentId] = (tokens, tokens.Count);

            // Build term frequency map for this document
            var termCounts = new Dictionary<string, int>();
            foreach (var token in tokens)
            {
                termCounts[token] = termCounts.TryGetValue(token, out var c) ? c + 1 : 1;
            }

            // Update inverted index and term frequencies
            foreach (var (term, count) in termCounts)
            {
                if (!_invertedIndex.TryGetValue(term, out var docSet))
                {
                    docSet = new HashSet<string>();
                    _invertedIndex[term] = docSet;
                }
                docSet.Add(documentId);

                if (!_termFrequencies.TryGetValue(term, out var tfMap))
                {
                    tfMap = new Dictionary<string, int>();
                    _termFrequencies[term] = tfMap;
                }
                tfMap[documentId] = count;
            }

            RecalculateAverageLength();
        }
    }

    public void RemoveDocument(string documentId)
    {
        lock (_lock)
        {
            RemoveDocumentInternal(documentId);
            RecalculateAverageLength();
        }
    }

    public void RemoveByPrefix(string documentIdPrefix)
    {
        lock (_lock)
        {
            var toRemove = _documents.Keys
                .Where(id => id.StartsWith(documentIdPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var id in toRemove)
            {
                RemoveDocumentInternal(id);
            }

            if (toRemove.Count > 0)
                RecalculateAverageLength();
        }
    }

    public List<(string DocumentId, double Score)> Search(string query, int topK)
    {
        var queryTokens = Tokenize(query);
        if (queryTokens.Count == 0)
            return [];

        // Deduplicate query tokens for IDF calculation
        var uniqueQueryTerms = queryTokens.Distinct().ToList();

        lock (_lock)
        {
            if (_documents.Count == 0)
                return [];

            var n = _documents.Count;
            var scores = new Dictionary<string, double>();

            foreach (var term in uniqueQueryTerms)
            {
                if (!_invertedIndex.TryGetValue(term, out var docSet))
                    continue;

                // IDF: log((N - df + 0.5) / (df + 0.5) + 1)
                var df = docSet.Count;
                var idf = Math.Log((n - df + 0.5) / (df + 0.5) + 1.0);

                if (!_termFrequencies.TryGetValue(term, out var tfMap))
                    continue;

                foreach (var docId in docSet)
                {
                    if (!tfMap.TryGetValue(docId, out var tf))
                        continue;

                    var docLen = _documents[docId].Length;
                    var normTf = (tf * (_k1 + 1.0)) /
                                 (tf + _k1 * (1.0 - _b + _b * docLen / _averageDocLength));

                    var termScore = idf * normTf;

                    scores[docId] = scores.TryGetValue(docId, out var existing)
                        ? existing + termScore
                        : termScore;
                }
            }

            return scores
                .OrderByDescending(kvp => kvp.Value)
                .Take(topK)
                .Select(kvp => (kvp.Key, kvp.Value))
                .ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _documents.Clear();
            _invertedIndex.Clear();
            _termFrequencies.Clear();
            _averageDocLength = 0;
        }
    }

    // ─── Tokenization ───────────────────────────────────────────────────

    /// <summary>
    /// Tokenizes text using whitespace splitting for Latin/mixed tokens,
    /// plus character bigrams for Korean (Hangul) sequences.
    /// This hybrid approach handles Korean's agglutinative morphology without
    /// requiring a full morphological analyzer.
    /// </summary>
    internal static List<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var normalized = text.ToLowerInvariant();
        var tokens = new List<string>();

        // Split on whitespace and punctuation, keeping meaningful tokens
        var words = WordSplitRegex().Split(normalized)
            .Where(w => w.Length > 0)
            .ToArray();

        foreach (var word in words)
        {
            if (ContainsHangul(word))
            {
                // Extract Korean characters and generate bigrams
                var hangulChars = new List<char>();
                var nonHangulBuffer = new List<char>();

                foreach (var ch in word)
                {
                    if (IsHangul(ch))
                    {
                        // Flush non-hangul buffer as a token
                        if (nonHangulBuffer.Count > 0)
                        {
                            var nonKorean = new string(nonHangulBuffer.ToArray());
                            if (nonKorean.Length >= 2)
                                tokens.Add(nonKorean);
                            nonHangulBuffer.Clear();
                        }
                        hangulChars.Add(ch);
                    }
                    else
                    {
                        // Flush hangul bigrams
                        if (hangulChars.Count > 0)
                        {
                            AddBigrams(tokens, hangulChars);
                            // Also add individual chars for single-char matches
                            if (hangulChars.Count == 1)
                                tokens.Add(hangulChars[0].ToString());
                            hangulChars.Clear();
                        }
                        nonHangulBuffer.Add(ch);
                    }
                }

                // Flush remaining
                if (hangulChars.Count > 0)
                {
                    AddBigrams(tokens, hangulChars);
                    if (hangulChars.Count == 1)
                        tokens.Add(hangulChars[0].ToString());
                }
                if (nonHangulBuffer.Count > 0)
                {
                    var remaining = new string(nonHangulBuffer.ToArray());
                    if (remaining.Length >= 2)
                        tokens.Add(remaining);
                }
            }
            else
            {
                // Latin/numeric token — add as-is if long enough
                if (word.Length >= 2)
                    tokens.Add(word);
            }
        }

        return tokens;
    }

    private static void AddBigrams(List<string> tokens, List<char> chars)
    {
        for (var i = 0; i < chars.Count - 1; i++)
        {
            tokens.Add($"{chars[i]}{chars[i + 1]}");
        }
    }

    private static bool ContainsHangul(string text)
    {
        foreach (var ch in text)
        {
            if (IsHangul(ch)) return true;
        }
        return false;
    }

    private static bool IsHangul(char ch)
    {
        // Hangul Syllables: U+AC00 - U+D7AF
        // Hangul Jamo: U+1100 - U+11FF
        // Hangul Compatibility Jamo: U+3130 - U+318F
        return ch is (>= '\uAC00' and <= '\uD7AF')
                  or (>= '\u1100' and <= '\u11FF')
                  or (>= '\u3130' and <= '\u318F');
    }

    // ─── Private helpers ────────────────────────────────────────────────

    private void RemoveDocumentInternal(string documentId)
    {
        if (!_documents.Remove(documentId))
            return;

        // Clean up inverted index and term frequencies
        var termsToRemove = new List<string>();

        foreach (var (term, docSet) in _invertedIndex)
        {
            docSet.Remove(documentId);
            if (docSet.Count == 0)
                termsToRemove.Add(term);
        }

        foreach (var term in termsToRemove)
        {
            _invertedIndex.Remove(term);
        }

        foreach (var (term, tfMap) in _termFrequencies)
        {
            tfMap.Remove(documentId);
        }

        // Clean up empty term frequency entries
        var emptyTfTerms = _termFrequencies
            .Where(kvp => kvp.Value.Count == 0)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var term in emptyTfTerms)
        {
            _termFrequencies.Remove(term);
        }
    }

    private void RecalculateAverageLength()
    {
        _averageDocLength = _documents.Count > 0
            ? _documents.Values.Average(d => d.Length)
            : 0;
    }

    [GeneratedRegex(@"[\s\p{P}]+", RegexOptions.Compiled)]
    private static partial Regex WordSplitRegex();
}
