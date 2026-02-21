using FabCopilot.RagService;
using FabCopilot.VectorStore.Models;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class HybridSearchMergeTests
{
    private static VectorSearchResult MakeResult(string id, float score, string text = "")
        => new(id, score, new Dictionary<string, object> { ["text"] = text });

    [Fact]
    public void MergeWithRrf_BothListsHaveSameDoc_HigherRrfScore()
    {
        // doc-A appears in both vector (#1) and BM25 (#1) => highest RRF
        // doc-B appears only in vector (#2) => lower RRF
        var vectorResults = new List<VectorSearchResult>
        {
            MakeResult("doc-A", 0.90f, "CMP pad replacement"),
            MakeResult("doc-B", 0.85f, "slurry flow rate"),
        };

        var bm25Results = new List<(string DocumentId, double Score)>
        {
            ("doc-A", 5.0),
            ("doc-C", 3.0), // BM25-only, no vector result => skipped
        };

        var merged = RagWorker.MergeWithRrf(vectorResults, bm25Results);

        // doc-A should be first (present in both lists)
        merged[0].Id.Should().Be("doc-A");
        // doc-B should be second (vector only)
        merged[1].Id.Should().Be("doc-B");
        // doc-C should be excluded (no vector data)
        merged.Should().HaveCount(2);
    }

    [Fact]
    public void MergeWithRrf_BM25BoostsLowerRankedVectorResult()
    {
        // doc-X is rank 3 in vector but rank 1 in BM25
        // doc-Y is rank 1 in vector but absent from BM25
        var vectorResults = new List<VectorSearchResult>
        {
            MakeResult("doc-Y", 0.95f, "unrelated text"),
            MakeResult("doc-Z", 0.90f, "some text"),
            MakeResult("doc-X", 0.85f, "CMP pad keyword rich text"),
        };

        var bm25Results = new List<(string DocumentId, double Score)>
        {
            ("doc-X", 8.0), // rank 1 in BM25
            ("doc-Z", 4.0), // rank 2 in BM25
        };

        var merged = RagWorker.MergeWithRrf(vectorResults, bm25Results);

        // doc-X: vec=1/(60+3) + bm25=1/(60+1) = 0.0159 + 0.0164 = 0.0323
        // doc-Y: vec=1/(60+1) + bm25=0 = 0.0164
        // doc-Z: vec=1/(60+2) + bm25=1/(60+2) = 0.0161 + 0.0161 = 0.0322
        // Order: doc-X > doc-Z > doc-Y
        merged[0].Id.Should().Be("doc-X");
        merged[1].Id.Should().Be("doc-Z");
        merged[2].Id.Should().Be("doc-Y");
    }

    [Fact]
    public void MergeWithRrf_EmptyBm25_RetainsVectorOrder()
    {
        var vectorResults = new List<VectorSearchResult>
        {
            MakeResult("doc-A", 0.90f),
            MakeResult("doc-B", 0.80f),
        };

        var bm25Results = new List<(string DocumentId, double Score)>();

        var merged = RagWorker.MergeWithRrf(vectorResults, bm25Results);

        merged.Should().HaveCount(2);
        merged[0].Id.Should().Be("doc-A");
        merged[1].Id.Should().Be("doc-B");
    }

    [Fact]
    public void MergeWithRrf_EmptyVector_ReturnsEmpty()
    {
        var vectorResults = new List<VectorSearchResult>();

        var bm25Results = new List<(string DocumentId, double Score)>
        {
            ("doc-A", 5.0),
        };

        var merged = RagWorker.MergeWithRrf(vectorResults, bm25Results);

        // BM25-only results are skipped (no payload data)
        merged.Should().BeEmpty();
    }

    [Fact]
    public void MergeWithRrf_CustomWeights_AffectsRanking()
    {
        var vectorResults = new List<VectorSearchResult>
        {
            MakeResult("vec-top", 0.95f, "vector favorite"),
            MakeResult("both", 0.80f, "appears in both"),
        };

        var bm25Results = new List<(string DocumentId, double Score)>
        {
            ("both", 8.0),  // rank 1 in BM25
        };

        // With high BM25 weight, "both" should be boosted
        var merged = RagWorker.MergeWithRrf(vectorResults, bm25Results,
            vectorWeight: 0.5f, bm25Weight: 2.0f);

        // "both": vec=0.5/(60+2) + bm25=2.0/(60+1) = 0.0081 + 0.0328 = 0.0409
        // "vec-top": vec=0.5/(60+1) + bm25=0 = 0.0082
        merged[0].Id.Should().Be("both");
    }

    [Fact]
    public void MergeWithRrf_LargeK_SmoothsRankDifferences()
    {
        var vectorResults = new List<VectorSearchResult>
        {
            MakeResult("doc-1", 0.95f),
            MakeResult("doc-2", 0.50f),
        };

        var bm25Results = new List<(string DocumentId, double Score)>
        {
            ("doc-2", 5.0),
            ("doc-1", 1.0),
        };

        // With default k=60, rank differences are smoothed
        var merged = RagWorker.MergeWithRrf(vectorResults, bm25Results, k: 60);

        // doc-1: 1/(60+1) + 1/(60+2) = 0.0164 + 0.0161 = 0.0325
        // doc-2: 1/(60+2) + 1/(60+1) = 0.0161 + 0.0164 = 0.0325
        // Very close scores with k=60
        merged.Should().HaveCount(2);
    }

    [Fact]
    public void MergeWithRrf_ManyDocuments_HandlesCorrectly()
    {
        var vectorResults = Enumerable.Range(0, 50)
            .Select(i => MakeResult($"doc-{i}", 1.0f - i * 0.01f, $"text {i}"))
            .ToList();

        var bm25Results = Enumerable.Range(0, 30)
            .Select(i => ($"doc-{49 - i}", (double)(30 - i))) // reverse order
            .ToList();

        var merged = RagWorker.MergeWithRrf(vectorResults, bm25Results);

        // Should have all 50 vector results
        merged.Should().HaveCount(50);
        // All IDs should be unique
        merged.Select(r => r.Id).Distinct().Should().HaveCount(50);
    }
}
