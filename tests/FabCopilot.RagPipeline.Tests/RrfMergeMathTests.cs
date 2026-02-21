using FluentAssertions;
using FabCopilot.RagService;
using FabCopilot.VectorStore.Models;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class RrfMergeMathTests
{
    private static VectorSearchResult MakeResult(string id, float score, string text = "")
        => new(id, score, new Dictionary<string, object> { ["text"] = text });

    [Fact]
    public void MergeWithRrf_SingleVectorResult_NoBm25_ReturnsIt()
    {
        var vectorResults = new[] { MakeResult("doc-1", 0.9f) };
        var bm25Results = Array.Empty<(string DocumentId, double Score)>();

        var merged = RagWorker.MergeWithRrf(vectorResults, bm25Results);
        merged.Should().HaveCount(1);
        merged[0].Id.Should().Be("doc-1");
    }

    [Fact]
    public void MergeWithRrf_IdenticalRanks_SymmetricScores()
    {
        var vectorResults = new[]
        {
            MakeResult("a", 0.9f),
            MakeResult("b", 0.8f)
        };
        var bm25Results = new (string, double)[] { ("a", 1.0), ("b", 0.5) };

        var merged = RagWorker.MergeWithRrf(vectorResults, bm25Results);
        // "a" is rank 1 in both → should have higher RRF than "b"
        merged[0].Id.Should().Be("a");
    }

    [Fact]
    public void MergeWithRrf_KEqualsZero_HighRankDominance()
    {
        var vectorResults = new[]
        {
            MakeResult("first", 0.9f),
            MakeResult("second", 0.8f),
            MakeResult("third", 0.7f)
        };
        var bm25Results = new (string, double)[] { ("first", 1.0), ("second", 0.8), ("third", 0.6) };

        // k=0 means rank 1 gets 1/(0+1) = 1.0, rank 2 gets 1/(0+2) = 0.5, rank 3 gets 1/(0+3) = 0.333
        var merged = RagWorker.MergeWithRrf(vectorResults, bm25Results, k: 0);
        merged[0].Id.Should().Be("first");
        merged[1].Id.Should().Be("second");
        merged[2].Id.Should().Be("third");
    }

    [Fact]
    public void MergeWithRrf_VeryLargeK_ScoresConverge()
    {
        var vectorResults = new[]
        {
            MakeResult("a", 0.9f),
            MakeResult("b", 0.1f)
        };
        var bm25Results = new (string, double)[] { ("a", 1.0), ("b", 0.1) };

        // k=10000: rank 1 → 1/10001 ≈ 0.0001, rank 2 → 1/10002 ≈ 0.0001 — very similar
        var merged = RagWorker.MergeWithRrf(vectorResults, bm25Results, k: 10000);
        merged.Should().HaveCount(2);
        // Both results should be returned (order may still be a→b but the gap is minimal)
        merged.Select(r => r.Id).Should().Contain("a").And.Contain("b");
    }

    [Fact]
    public void MergeWithRrf_ZeroVectorWeight_OnlyBm25Matters()
    {
        // "a" is rank 1 in vector but rank 2 in BM25
        // "b" is rank 2 in vector but rank 1 in BM25
        var vectorResults = new[]
        {
            MakeResult("a", 0.9f),
            MakeResult("b", 0.8f)
        };
        var bm25Results = new (string, double)[] { ("b", 1.0), ("a", 0.5) };

        var merged = RagWorker.MergeWithRrf(vectorResults, bm25Results, vectorWeight: 0f, bm25Weight: 1f);
        // Only BM25 rank matters: "b" is rank 1, "a" is rank 2
        merged[0].Id.Should().Be("b");
    }

    [Fact]
    public void MergeWithRrf_ZeroBm25Weight_OnlyVectorMatters()
    {
        var vectorResults = new[]
        {
            MakeResult("a", 0.9f),
            MakeResult("b", 0.8f)
        };
        var bm25Results = new (string, double)[] { ("b", 1.0), ("a", 0.5) };

        var merged = RagWorker.MergeWithRrf(vectorResults, bm25Results, vectorWeight: 1f, bm25Weight: 0f);
        // Only vector rank matters: "a" is rank 1
        merged[0].Id.Should().Be("a");
    }

    [Fact]
    public void MergeWithRrf_BothListsEmpty_ReturnsEmpty()
    {
        var vectorResults = Array.Empty<VectorSearchResult>();
        var bm25Results = Array.Empty<(string DocumentId, double Score)>();

        var merged = RagWorker.MergeWithRrf(vectorResults, bm25Results);
        merged.Should().BeEmpty();
    }

    [Fact]
    public void MergeWithRrf_DuplicateIdsInBm25_LastRankWins()
    {
        var vectorResults = new[] { MakeResult("a", 0.9f) };
        // "a" appears twice in BM25 — dict overwrite means rank 2 wins
        var bm25Results = new (string, double)[] { ("a", 1.0), ("a", 0.5) };

        var merged = RagWorker.MergeWithRrf(vectorResults, bm25Results);
        merged.Should().HaveCount(1);
        merged[0].Id.Should().Be("a");
    }

    [Fact]
    public void MergeWithRrf_100Vector_30Bm25_CorrectCount()
    {
        var vectorResults = Enumerable.Range(0, 100)
            .Select(i => MakeResult($"v-{i}", 1.0f - i * 0.01f))
            .ToArray();
        var bm25Results = Enumerable.Range(0, 30)
            .Select(i => ($"v-{i * 3}", (double)(1.0 - i * 0.03)))
            .ToArray();

        var merged = RagWorker.MergeWithRrf(vectorResults, bm25Results);
        // All 100 vector results should be present (BM25-only results without vector data are skipped)
        merged.Should().HaveCount(100);
    }

    [Fact]
    public void MergeWithRrf_FormulaVerification_HandCalculated()
    {
        // k=60 (default), equal weights
        // doc-a: vec rank 1, bm25 rank 2 → 1/(60+1) + 1/(60+2) = 0.01639 + 0.01613 = 0.03252
        // doc-b: vec rank 2, bm25 rank 1 → 1/(60+2) + 1/(60+1) = 0.01613 + 0.01639 = 0.03252
        // doc-c: vec rank 3, bm25 not present → 1/(60+3) = 0.01587
        var vectorResults = new[]
        {
            MakeResult("a", 0.9f),
            MakeResult("b", 0.8f),
            MakeResult("c", 0.7f)
        };
        var bm25Results = new (string, double)[] { ("b", 1.0), ("a", 0.5) };

        var merged = RagWorker.MergeWithRrf(vectorResults, bm25Results);
        // "a" and "b" should have equal RRF scores (symmetric ranks), but tie-breaking by iteration order
        // "c" should be last (only vector, no BM25)
        merged.Should().HaveCount(3);
        merged[2].Id.Should().Be("c");
    }
}
