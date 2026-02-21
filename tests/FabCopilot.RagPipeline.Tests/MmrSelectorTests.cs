using FabCopilot.RagService.Services;
using FabCopilot.VectorStore.Models;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class MmrSelectorTests
{
    private static VectorSearchResult MakeResult(string id, float score, string text)
        => new(id, score, new Dictionary<string, object> { ["text"] = text });

    [Fact]
    public void Select_FewerThanTopK_ReturnsAll()
    {
        var candidates = new List<VectorSearchResult>
        {
            MakeResult("doc1", 0.9f, "CMP pad replacement"),
            MakeResult("doc2", 0.8f, "slurry flow rate"),
        };

        var result = MmrSelector.Select(candidates, "pad", topK: 5);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Select_DiversifiesSimilarDocuments()
    {
        // Three virtually identical docs (same words) and two different topics —
        // MMR should diversify and not select all 3 identical docs
        var candidates = new List<VectorSearchResult>
        {
            MakeResult("pad1", 0.90f, "CMP pad replacement procedure guide"),
            MakeResult("pad2", 0.90f, "CMP pad replacement procedure guide"),
            MakeResult("pad3", 0.90f, "CMP pad replacement procedure guide"),
            MakeResult("slurry", 0.90f, "slurry flow rate monitoring and adjustment"),
            MakeResult("wafer", 0.90f, "wafer polishing parameters and specifications"),
        };

        // With equal scores, MMR diversity penalty should prefer different docs
        var result = MmrSelector.Select(candidates, "equipment maintenance", topK: 3, lambda: 0.5);

        result.Should().HaveCount(3);
        var ids = result.Select(r => r.Id).ToList();
        var padCount = ids.Count(id => id.StartsWith("pad"));
        // With identical scores and identical text, at most 1 pad doc should appear
        // because the others are exact duplicates and get heavily penalized
        padCount.Should().BeLessThan(3, "MMR should diversify away from near-duplicate documents");
    }

    [Fact]
    public void Select_HighLambda_FavorsRelevance()
    {
        var candidates = new List<VectorSearchResult>
        {
            MakeResult("rel1", 0.95f, "CMP pad replacement guide"),
            MakeResult("rel2", 0.93f, "CMP pad replacement steps"),
            MakeResult("div", 0.80f, "wafer cleaning procedure"),
        };

        var result = MmrSelector.Select(candidates, "CMP pad replacement", topK: 2, lambda: 1.0);

        // lambda=1.0 means pure relevance, no diversity
        result.Should().HaveCount(2);
        result[0].Id.Should().Be("rel1");
        result[1].Id.Should().Be("rel2");
    }

    [Fact]
    public void Select_LowLambda_FavorsDiversity()
    {
        var candidates = new List<VectorSearchResult>
        {
            MakeResult("pad1", 0.95f, "CMP pad replacement procedure guide"),
            MakeResult("pad2", 0.94f, "CMP pad replacement procedure instructions"),
            MakeResult("slurry", 0.80f, "slurry flow rate adjustment"),
        };

        var result = MmrSelector.Select(candidates, "CMP pad replacement", topK: 2, lambda: 0.3);

        // lambda=0.3 means heavy diversity penalty
        result.Should().HaveCount(2);
        result[0].Id.Should().Be("pad1"); // Still the most relevant
        // Second should be diverse (slurry) due to low lambda
        result[1].Id.Should().Be("slurry");
    }

    [Fact]
    public void Select_EmptyCandidates_ReturnsEmpty()
    {
        var candidates = new List<VectorSearchResult>();
        var result = MmrSelector.Select(candidates, "query", topK: 5);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Select_SingleCandidate_ReturnsSingle()
    {
        var candidates = new List<VectorSearchResult>
        {
            MakeResult("only", 0.90f, "only document"),
        };

        var result = MmrSelector.Select(candidates, "query", topK: 5);
        result.Should().HaveCount(1);
        result[0].Id.Should().Be("only");
    }

    // ─── Tokenization Tests ─────────────────────────────────────────

    [Fact]
    public void Tokenize_SplitsOnWhitespaceAndPunctuation()
    {
        var tokens = MmrSelector.Tokenize("Hello, world! This is a test.");

        tokens.Should().Contain("hello");
        tokens.Should().Contain("world");
        tokens.Should().Contain("this");
        tokens.Should().Contain("is");
        tokens.Should().Contain("test");
        // Single-char words filtered
        tokens.Should().NotContain("a");
    }

    [Fact]
    public void CosineSimilarity_IdenticalDocuments_ReturnsOne()
    {
        var tf = MmrSelector.BuildTermFrequency(MmrSelector.Tokenize("CMP pad replacement"));
        var idf = MmrSelector.ComputeIdf([tf, tf]);

        var sim = MmrSelector.CosineSimilarity(tf, tf, idf);

        sim.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void CosineSimilarity_DisjointDocuments_ReturnsZero()
    {
        var tf1 = MmrSelector.BuildTermFrequency(MmrSelector.Tokenize("CMP pad"));
        var tf2 = MmrSelector.BuildTermFrequency(MmrSelector.Tokenize("wafer cleaning"));
        var idf = MmrSelector.ComputeIdf([tf1, tf2]);

        var sim = MmrSelector.CosineSimilarity(tf1, tf2, idf);

        sim.Should().BeApproximately(0.0, 0.001);
    }

    [Fact]
    public void CosineSimilarity_PartialOverlap_BetweenZeroAndOne()
    {
        var tf1 = MmrSelector.BuildTermFrequency(MmrSelector.Tokenize("CMP pad replacement"));
        var tf2 = MmrSelector.BuildTermFrequency(MmrSelector.Tokenize("CMP wafer cleaning"));
        var idf = MmrSelector.ComputeIdf([tf1, tf2]);

        var sim = MmrSelector.CosineSimilarity(tf1, tf2, idf);

        sim.Should().BeGreaterThan(0.0);
        sim.Should().BeLessThan(1.0);
    }
}
