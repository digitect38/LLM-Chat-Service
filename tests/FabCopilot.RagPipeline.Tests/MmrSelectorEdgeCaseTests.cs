using FluentAssertions;
using FabCopilot.RagService.Services;
using FabCopilot.VectorStore.Models;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class MmrSelectorEdgeCaseTests
{
    private static VectorSearchResult MakeResult(string id, float score, string text)
        => new(id, score, new Dictionary<string, object> { ["text"] = text });

    [Fact]
    public void BuildTermFrequency_EmptyTokenList_ReturnsEmpty()
    {
        var tf = MmrSelector.BuildTermFrequency([]);
        tf.Should().BeEmpty();
    }

    [Fact]
    public void BuildTermFrequency_RepeatedTokens_CountedCorrectly()
    {
        var tf = MmrSelector.BuildTermFrequency(["apple", "apple", "banana"]);
        tf["apple"].Should().Be(2);
        tf["banana"].Should().Be(1);
    }

    [Fact]
    public void ComputeIdf_SingleDocument_UniformIdf()
    {
        var docTfs = new List<Dictionary<string, int>>
        {
            new() { ["term1"] = 1, ["term2"] = 2 }
        };

        var idf = MmrSelector.ComputeIdf(docTfs);
        // With 1 doc, all terms appear in 1/1 docs, so IDF should be identical
        idf["term1"].Should().BeApproximately(idf["term2"], 0.001);
    }

    [Fact]
    public void ComputeIdf_EmptyDocumentList_ReturnsEmpty()
    {
        var idf = MmrSelector.ComputeIdf([]);
        idf.Should().BeEmpty();
    }

    [Fact]
    public void ComputeIdf_RareTerms_HigherIdf()
    {
        var docTfs = new List<Dictionary<string, int>>
        {
            new() { ["common"] = 1, ["rare"] = 1 },
            new() { ["common"] = 1 },
            new() { ["common"] = 1 }
        };

        var idf = MmrSelector.ComputeIdf(docTfs);
        // "rare" appears in 1/3 docs, "common" in 3/3 → rare should have higher IDF
        idf["rare"].Should().BeGreaterThan(idf["common"]);
    }

    [Fact]
    public void CosineSimilarity_OneEmptyTf_ReturnsZero()
    {
        var tf1 = new Dictionary<string, int> { ["word"] = 1 };
        var tf2 = new Dictionary<string, int>();
        var idf = new Dictionary<string, double> { ["word"] = 1.0 };

        var sim = MmrSelector.CosineSimilarity(tf1, tf2, idf);
        sim.Should().Be(0.0);
    }

    [Fact]
    public void CosineSimilarity_IdenticalDocs_ReturnsOne()
    {
        var tf = new Dictionary<string, int> { ["pad"] = 2, ["slurry"] = 1 };
        var idf = new Dictionary<string, double> { ["pad"] = 1.0, ["slurry"] = 1.0 };

        var sim = MmrSelector.CosineSimilarity(tf, tf, idf);
        sim.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void CosineSimilarity_Symmetric()
    {
        var tf1 = new Dictionary<string, int> { ["pad"] = 2, ["slurry"] = 1 };
        var tf2 = new Dictionary<string, int> { ["pad"] = 1, ["conditioner"] = 3 };
        var idf = new Dictionary<string, double> { ["pad"] = 1.0, ["slurry"] = 1.5, ["conditioner"] = 1.2 };

        var sim12 = MmrSelector.CosineSimilarity(tf1, tf2, idf);
        var sim21 = MmrSelector.CosineSimilarity(tf2, tf1, idf);
        sim12.Should().BeApproximately(sim21, 0.0001);
    }

    [Fact]
    public void Select_AllIdenticalDocs_ReturnsTopK()
    {
        var candidates = Enumerable.Range(0, 5)
            .Select(i => MakeResult($"doc-{i}", 0.8f, "identical text for all documents"))
            .ToList();

        var selected = MmrSelector.Select(candidates, "identical text", topK: 3);
        selected.Should().HaveCount(3);
    }

    [Fact]
    public void Tokenize_KoreanText_ProducesBigrams()
    {
        var tokens = MmrSelector.Tokenize("패드 교체 절차");
        // Korean words should be tokenized as individual tokens split by space
        tokens.Should().NotBeEmpty();
        tokens.Should().Contain("패드");
    }
}
