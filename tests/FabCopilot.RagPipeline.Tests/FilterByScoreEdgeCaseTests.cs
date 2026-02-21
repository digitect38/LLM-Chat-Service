using FluentAssertions;
using FabCopilot.RagService;
using FabCopilot.VectorStore.Models;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class FilterByScoreEdgeCaseTests
{
    private static VectorSearchResult MakeResult(string id, float score)
        => new(id, score, new Dictionary<string, object> { ["text"] = $"text for {id}" });

    [Fact]
    public void FilterByScore_NegativeThreshold_ReturnsAll()
    {
        var results = new[] { MakeResult("a", 0.1f), MakeResult("b", 0.5f), MakeResult("c", 0.9f) };
        var filtered = RagWorker.FilterByScore(results, -1.0f);
        filtered.Should().HaveCount(3);
    }

    [Fact]
    public void FilterByScore_AllExactlyAtThreshold_ReturnsAll()
    {
        var results = new[] { MakeResult("a", 0.5f), MakeResult("b", 0.5f), MakeResult("c", 0.5f) };
        var filtered = RagWorker.FilterByScore(results, 0.5f);
        filtered.Should().HaveCount(3);
    }

    [Fact]
    public void FilterByScore_VeryHighThreshold_ReturnsNone()
    {
        var results = new[] { MakeResult("a", 0.9f), MakeResult("b", 1.0f) };
        var filtered = RagWorker.FilterByScore(results, 1.01f);
        filtered.Should().BeEmpty();
    }

    [Fact]
    public void FilterByScore_PreservesOriginalOrder()
    {
        var results = new[]
        {
            MakeResult("c", 0.9f),
            MakeResult("a", 0.7f),
            MakeResult("b", 0.8f)
        };
        var filtered = RagWorker.FilterByScore(results, 0.5f);
        filtered.Select(r => r.Id).Should().ContainInOrder("c", "a", "b");
    }

    [Fact]
    public void FilterByScore_LargeCollection_10000Items()
    {
        var results = Enumerable.Range(0, 10_000)
            .Select(i => MakeResult($"doc-{i}", i / 10_000f))
            .ToList();

        var filtered = RagWorker.FilterByScore(results, 0.5f);
        filtered.Should().OnlyContain(r => r.Score >= 0.5f);
        filtered.Count.Should().BeGreaterThan(0);
        filtered.Count.Should().BeLessThan(10_000);
    }

    [Fact]
    public void FilterByScore_SingleResult_AtThreshold_Included()
    {
        var results = new[] { MakeResult("x", 0.55f) };
        var filtered = RagWorker.FilterByScore(results, 0.55f);
        filtered.Should().HaveCount(1);
    }

    [Fact]
    public void FilterByScore_SingleResult_BelowThreshold_Excluded()
    {
        var results = new[] { MakeResult("x", 0.549f) };
        var filtered = RagWorker.FilterByScore(results, 0.55f);
        filtered.Should().BeEmpty();
    }

    [Fact]
    public void FilterByScore_FloatPrecision_BoundaryBehavior()
    {
        // 0.54999998f is < 0.55f in IEEE 754, so should be excluded
        var results = new[] { MakeResult("edge", 0.54999998f) };
        var filtered = RagWorker.FilterByScore(results, 0.55f);
        filtered.Should().BeEmpty();
    }
}
