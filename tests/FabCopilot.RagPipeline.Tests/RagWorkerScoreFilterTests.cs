using FabCopilot.RagService;
using FabCopilot.VectorStore.Models;
using FluentAssertions;
using Xunit;

namespace FabCopilot.RagPipeline.Tests;

public class RagWorkerScoreFilterTests
{
    private static VectorSearchResult MakeResult(float score)
        => new("id", score, new Dictionary<string, object> { ["text"] = "test" });

    [Fact]
    public void FilterByScore_RemovesBelowThreshold()
    {
        var results = new List<VectorSearchResult>
        {
            MakeResult(0.1f),
            MakeResult(0.5f),
            MakeResult(0.9f)
        };

        var filtered = RagWorker.FilterByScore(results, 0.3f);

        filtered.Should().HaveCount(2);
        filtered.Should().OnlyContain(r => r.Score >= 0.3f);
    }

    [Fact]
    public void FilterByScore_IncludesBoundaryValue()
    {
        var results = new List<VectorSearchResult>
        {
            MakeResult(0.3f),
            MakeResult(0.29f)
        };

        var filtered = RagWorker.FilterByScore(results, 0.3f);

        filtered.Should().HaveCount(1);
        filtered[0].Score.Should().Be(0.3f);
    }

    [Fact]
    public void FilterByScore_EmptyInput_ReturnsEmpty()
    {
        var results = new List<VectorSearchResult>();

        var filtered = RagWorker.FilterByScore(results, 0.3f);

        filtered.Should().BeEmpty();
    }

    [Fact]
    public void FilterByScore_ZeroThreshold_ReturnsAll()
    {
        var results = new List<VectorSearchResult>
        {
            MakeResult(0.0f),
            MakeResult(0.01f),
            MakeResult(0.99f)
        };

        var filtered = RagWorker.FilterByScore(results, 0.0f);

        filtered.Should().HaveCount(3);
    }

    [Fact]
    public void FilterByScore_OneThreshold_ReturnsOnlyPerfect()
    {
        var results = new List<VectorSearchResult>
        {
            MakeResult(0.99f),
            MakeResult(1.0f)
        };

        var filtered = RagWorker.FilterByScore(results, 1.0f);

        filtered.Should().HaveCount(1);
        filtered[0].Score.Should().Be(1.0f);
    }

    [Fact]
    public void FilterByScore_AllBelowThreshold_ReturnsEmpty()
    {
        var results = new List<VectorSearchResult>
        {
            MakeResult(0.1f),
            MakeResult(0.2f)
        };

        var filtered = RagWorker.FilterByScore(results, 0.5f);

        filtered.Should().BeEmpty();
    }
}
